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
    /// How many channels the timeline will draw.
    /// </summary>
    /// <remarks>
    /// A real subscription lists tens of thousands, and a timeline is a grid of blocks rather than a list
    /// of rows — a window over 17,000 channels would be several hundred thousand elements. The limit is
    /// reported on screen rather than applied silently, because a guide that quietly stops after 200
    /// channels reads as a guide with missing channels.
    /// </remarks>
    public const int MaximumRows = 200;

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
            Notice = DescribeCoverage(shownRows: 0, channelsWithGuide: 0);
            return;
        }

        // Asked for each time rather than read from the channels handed over. Those were loaded when the
        // catalogue was shown, which is before any guide import finished — reading their link is why the
        // timeline reported "no guide data" for a guide that had just been imported successfully.
        var links = await _catalogue.GetGuideLinksAsync(_source.Id, cancellationToken).ConfigureAwait(true);

        var rowChannels = _channels
            .Where(channel => links.ContainsKey(channel.Id))
            .Take(MaximumRows)
            .ToList();

        Notice = DescribeCoverage(rowChannels.Count, _channels.Count(channel => links.ContainsKey(channel.Id)));

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

    private string DescribeCoverage(int shownRows, int channelsWithGuide)
    {
        if (channelsWithGuide == 0)
        {
            return _channels.Count == 0
                ? "No channels are listed."
                : "None of the listed channels has guide data. Load the guide, or narrow the filter to "
                    + "channels the guide covers.";
        }

        return shownRows < channelsWithGuide
            ? $"Showing {shownRows} of {channelsWithGuide} channels with guide data. Filter the channel "
                + "list to see the others."
            : string.Empty;
    }
}
