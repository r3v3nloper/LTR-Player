using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Player.Wpf;

/// <summary>
/// The programme timeline: one line per channel, a window of a few hours, and the detail of whatever is
/// selected.
/// </summary>
/// <remarks>
/// <para>
/// Knows nothing of the channel list. The channels to show are handed in by the view model that composes
/// both, which is what keeps the two halves of the shell independent of each other.
/// </para>
/// <para>
/// The window is moved by command rather than scrolled. Moving it is not a scroll: it changes which
/// programmes have to be loaded, and expressing it as time makes the loading obvious instead of hiding it
/// behind a scrollbar.
/// </para>
/// </remarks>
public sealed partial class GuideViewModel : ObservableObject
{
    /// <summary>
    /// How many channels the timeline draws at once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A real subscription lists tens of thousands, and a timeline is a grid of blocks rather than a list of
    /// rows — a window over 17,000 channels would be several hundred thousand elements. So it draws a page,
    /// and the channels beyond it are reached by moving to the next one.
    /// </para>
    /// <para>
    /// This used to be a cap: the first 200 and nothing else, with the rest reachable only by narrowing the
    /// channel list until they fell inside it. Two hundred is kept as the page size because it is the figure
    /// this timeline is known to draw comfortably.
    /// </para>
    /// </remarks>
    public const int RowsPerPage = 200;

    private static readonly TimeSpan Step = TimeSpan.FromMinutes(30);

    private readonly IGuideCatalogue _catalogue;
    private readonly TimeProvider _timeProvider;

    private PlaylistSource? _source;
    private IReadOnlyList<Channel> _channels = [];

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private GuideTimeline _timeline = GuideTimeline.Default;

    [ObservableProperty]
    private GuideProgrammeViewModel? _selectedProgramme;

    /// <summary>Where the current moment falls in the window, for the marker line.</summary>
    [ObservableProperty]
    private double _nowPosition;

    [ObservableProperty]
    private bool _isNowVisible;

    [ObservableProperty]
    private string _notice = string.Empty;

    /// <summary>
    /// Index of the first channel the current page draws, among those the guide covers.
    /// </summary>
    /// <remarks>
    /// Paged by command rather than scrolled, for the reason the time window is: moving it changes which
    /// programmes have to be fetched, and a scrollbar that silently issues a query per flick hides that. It
    /// also spares the timeline a data-virtualising collection whose rows would appear empty and fill in
    /// afterwards — in a grid where every row is already a mosaic of blocks, that reads as a fault.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowEarlierChannelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowLaterChannelsCommand))]
    private int _rowOffset;

    /// <summary>
    /// How many of the source's channels the guide covers at all, which is what the pages run over.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowEarlierChannelsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowLaterChannelsCommand))]
    private int _channelsWithGuide;

    public GuideViewModel(IGuideCatalogue catalogue, TimeProvider timeProvider)
    {
        _catalogue = catalogue;
        _timeProvider = timeProvider;
    }

    public ObservableCollection<GuideRowViewModel> Rows { get; } = [];

    /// <summary>Half-hour labels across the top, positioned by the same arithmetic as the blocks.</summary>
    public ObservableCollection<GuideTimeMarker> TimeMarkers { get; } = [];

    /// <summary>
    /// Takes the channels the list is showing and prepares to draw them.
    /// </summary>
    /// <remarks>
    /// Does not load anything by itself. The window opens on the current moment when it is shown, and
    /// loading before that would fetch programmes for a panel nobody has asked to see.
    /// </remarks>
    public void Attach(PlaylistSource? source, IReadOnlyList<Channel> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);

        _source = source;
        _channels = channels;

        // A different set of channels makes the current page meaningless: page three of a filtered list is
        // not page three of the same list unfiltered.
        RowOffset = 0;
    }

    /// <summary>
    /// Opens the timeline on the current moment, for the channels given.
    /// </summary>
    /// <remarks>
    /// Positioning the window is done here rather than by the caller, because it needs the clock and this
    /// is what holds it. Having the caller compute "now" is how the window ends up somewhere other than
    /// where the guide's own marker thinks it is.
    /// </remarks>
    public async Task ShowAsync(
        PlaylistSource? source,
        IReadOnlyList<Channel> channels,
        CancellationToken cancellationToken)
    {
        Attach(source, channels);

        Timeline = Timeline.StartingAt(_timeProvider.GetUtcNow());
        IsVisible = true;

        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    public void Hide()
    {
        IsVisible = false;
    }

    /// <summary>
    /// Draws the current window.
    /// </summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Rows.Clear();
        TimeMarkers.Clear();
        SelectedProgramme = null;

        BuildTimeMarkers();
        UpdateNowMarker();

        if (_source is null)
        {
            ChannelsWithGuide = 0;
            Notice = DescribeCoverage(shownRows: 0);
            return;
        }

        // Asked for each time rather than read from the channels handed over. Those were loaded when the
        // catalogue was shown, which is before any guide import finished — reading their link is why the
        // timeline reported "no guide data" for a guide that had just been imported successfully.
        var links = await _catalogue.GetGuideLinksAsync(_source.Id, cancellationToken).ConfigureAwait(true);

        var covered = _channels.Where(channel => links.ContainsKey(channel.Id)).ToList();
        ChannelsWithGuide = covered.Count;

        // Clamped rather than trusted: the page can outlive the set it indexes into — a guide import adds
        // covered channels, and moving the time window reloads on whatever page was showing.
        RowOffset = covered.Count == 0 ? 0 : Math.Min(RowOffset, LastPageOffset(covered.Count));

        var rowChannels = covered.Skip(RowOffset).Take(RowsPerPage).ToList();

        Notice = DescribeCoverage(rowChannels.Count);

        if (rowChannels.Count == 0)
        {
            return;
        }

        var guideChannelIds = rowChannels.Select(channel => links[channel.Id]).Distinct().ToList();

        var programmes = await _catalogue
            .GetGuideProgrammesAsync(guideChannelIds, Timeline.StartUtc, Timeline.EndUtc, cancellationToken)
            .ConfigureAwait(true);

        var programmesByGuideChannel = programmes
            .GroupBy(entry => entry.GuideChannelId)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var channel in rowChannels)
        {
            var entries = programmesByGuideChannel.GetValueOrDefault(links[channel.Id], []);

            Rows.Add(new GuideRowViewModel(
                channel,
                [.. entries.Select(entry => new GuideProgrammeViewModel(entry, Timeline))]));
        }
    }

    /// <summary>
    /// Moves the marker line without redrawing anything, which is what the window's timer calls.
    /// </summary>
    public void UpdateNowMarker()
    {
        var now = _timeProvider.GetUtcNow();

        IsNowVisible = now >= Timeline.StartUtc && now < Timeline.EndUtc;
        NowPosition = IsNowVisible ? Timeline.PositionOf(now) : 0;
    }

    [RelayCommand]
    private Task MoveEarlierAsync(CancellationToken cancellationToken)
    {
        Timeline = Timeline.ShiftedBy(-Step);
        return LoadAsync(cancellationToken);
    }

    [RelayCommand]
    private Task MoveLaterAsync(CancellationToken cancellationToken)
    {
        Timeline = Timeline.ShiftedBy(Step);
        return LoadAsync(cancellationToken);
    }

    [RelayCommand]
    private Task JumpToNowAsync(CancellationToken cancellationToken)
    {
        Timeline = Timeline.StartingAt(_timeProvider.GetUtcNow());
        return LoadAsync(cancellationToken);
    }

    /// <summary>
    /// Draws the page of channels before this one.
    /// </summary>
    /// <remarks>
    /// The channel axis is moved the same way the time axis is, and for the same reason: each move is a
    /// fetch, and saying so with a button is more honest than a scrollbar that hides it.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanShowEarlierChannels))]
    private Task ShowEarlierChannelsAsync(CancellationToken cancellationToken)
    {
        RowOffset = Math.Max(0, RowOffset - RowsPerPage);
        return LoadAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanShowLaterChannels))]
    private Task ShowLaterChannelsAsync(CancellationToken cancellationToken)
    {
        RowOffset = Math.Min(LastPageOffset(ChannelsWithGuide), RowOffset + RowsPerPage);
        return LoadAsync(cancellationToken);
    }

    private bool CanShowEarlierChannels()
    {
        return RowOffset > 0;
    }

    private bool CanShowLaterChannels()
    {
        return RowOffset + RowsPerPage < ChannelsWithGuide;
    }

    /// <summary>
    /// Where the last page starts, so a page is never opened past the end of the channels.
    /// </summary>
    private static int LastPageOffset(int channelsWithGuide)
    {
        if (channelsWithGuide <= RowsPerPage)
        {
            return 0;
        }

        return (channelsWithGuide - 1) / RowsPerPage * RowsPerPage;
    }

    [RelayCommand]
    private void SelectProgramme(GuideProgrammeViewModel? programme)
    {
        SelectedProgramme = programme;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        SelectedProgramme = null;
    }

    private void BuildTimeMarkers()
    {
        for (var offset = TimeSpan.Zero; offset < Timeline.Duration; offset += Step)
        {
            var instant = Timeline.StartUtc + offset;

            TimeMarkers.Add(new GuideTimeMarker(
                Timeline.PositionOf(instant),
                instant.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)));
        }
    }

    /// <summary>
    /// Says where in the covered channels this page sits, and why the timeline is empty when it is.
    /// </summary>
    /// <remarks>
    /// Silent when everything covered is on screen, which is the common case for a filtered list and for a
    /// modest subscription. When it is not, it states the range rather than a count: "showing 200 of 4,531"
    /// answers how many are missing but not which, and the whole point of paging is that the viewer can now
    /// go and see them.
    /// </remarks>
    private string DescribeCoverage(int shownRows)
    {
        if (ChannelsWithGuide == 0)
        {
            return _channels.Count == 0
                ? "No channels are listed."
                : "None of the listed channels has guide data. Load the guide, or narrow the filter to "
                    + "channels the guide covers.";
        }

        if (shownRows >= ChannelsWithGuide)
        {
            return string.Empty;
        }

        var first = RowOffset + 1;
        var last = RowOffset + shownRows;

        return $"Channels {first}–{last} of {ChannelsWithGuide} with guide data.";
    }
}
