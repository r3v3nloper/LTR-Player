using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Presents one source's series, and the seasons and episodes of the one that is open.
/// </summary>
/// <remarks>
/// The search, the category picker and the count come from <see cref="CatalogueSectionViewModel{TRow}"/>.
/// What is particular to series is here, and it is the expensive part: opening one fetches its seasons from
/// the panel the first time, which is why <see cref="LoadSelectedAsync"/> exists and is awaited by the shell
/// rather than run from a property setter.
/// </remarks>
public sealed partial class SeriesCatalogueViewModel : CatalogueSectionViewModel<SeriesItemViewModel>
{
    private readonly IVodDetailService _detail;
    private readonly ILogger<SeriesCatalogueViewModel> _logger;

    [ObservableProperty]
    private SeriesItemViewModel? _selectedSeries;

    /// <summary>The selected series once its seasons are known, or null while none is open.</summary>
    [ObservableProperty]
    private Series? _openSeries;

    [ObservableProperty]
    private SeasonChoice? _selectedSeason;

    /// <summary>
    /// The episode row the viewer has picked out, which is what double-clicking plays.
    /// </summary>
    /// <remarks>
    /// The row's own play button carries the episode as a command parameter and needs no selection. This
    /// exists for the gesture every other list in the window answers to, and whose absence here made an
    /// episode list that looked interactive and did nothing.
    /// </remarks>
    [ObservableProperty]
    private EpisodeItemViewModel? _selectedEpisode;

    /// <summary>Whether a season's episodes are still being fetched, so the view can say so.</summary>
    [ObservableProperty]
    private bool _isLoadingEpisodes;

    public SeriesCatalogueViewModel(
        ISourceStore sources,
        IVodCatalogue catalogue,
        IVodDetailService detail,
        ILogger<SeriesCatalogueViewModel> logger)
        : base(sources, catalogue, ContentKind.Series)
    {
        _detail = detail;
        _logger = logger;
    }

    /// <summary>The series currently shown, named as the view reads it.</summary>
    public IReadOnlyList<SeriesItemViewModel> Series => Rows;

    public ObservableCollection<SeasonChoice> Seasons { get; } = [];

    public ObservableCollection<EpisodeItemViewModel> Episodes { get; } = [];


    protected override string EntryNoun => "series";

    /// <summary>
    /// Opens the selected series, fetching its seasons when the stored copy will not do.
    /// </summary>
    public async Task LoadSelectedAsync(CancellationToken cancellationToken)
    {
        if (Source is not { } source || SelectedSeries is not { } selected)
        {
            ClearSelection();
            return;
        }

        IsLoadingEpisodes = true;

        try
        {
            var series = await _detail
                .GetSeriesAsync(source, selected.Id, cancellationToken)
                .ConfigureAwait(true);

            // Discarded when the selection moved on while the panel was being asked, which is what stops a
            // slow answer replacing the episodes of a series the viewer has already left.
            if (series is null || SelectedSeries?.Id != selected.Id)
            {
                return;
            }

            ShowSeasons(series);

            if (Seasons.Count == 0)
            {
                Notice = $"{series.Name} has no episodes this player could read. The log records why.";
            }
        }
        catch (OperationCanceledException)
        {
            // The selection moved on, or the window is closing.
        }
        catch (Exception exception)
        {
            PlayerLog.SeriesDetailFailed(_logger, exception, selected.Name);
            Notice = $"{selected.Name}'s episodes could not be loaded. Details are in the log.";
        }
        finally
        {
            IsLoadingEpisodes = false;
        }
    }

    /// <summary>
    /// Rereads the open series, which is how a position recorded during playback reaches the episode rows.
    /// </summary>
    public async Task RefreshOpenSeriesAsync(CancellationToken cancellationToken)
    {
        if (Source is not { } source || OpenSeries is not { } open)
        {
            return;
        }

        // Goes through the detail service rather than reaching for the store, and costs nothing extra: the
        // stored detail is current by definition here, so this is a read rather than a fetch.
        var reloaded = await _detail.GetSeriesAsync(source, open.Id, cancellationToken).ConfigureAwait(true);

        if (reloaded is null || OpenSeries?.Id != reloaded.Id)
        {
            return;
        }

        ShowSeasons(reloaded);
    }

    /// <summary>
    /// The episode <paramref name="offset"/> places from the given one, or <see langword="null"/> at either
    /// end of the series.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asks the store rather than reading <see cref="Episodes"/>, and that is the whole point of it. The rows
    /// hold one season of one series and only while it is open, so a walk over them could not answer across a
    /// season boundary and could not answer at all for an episode resumed from the continue-watching list —
    /// which is the case the viewer reported, and where next-episode used to change the live channel.
    /// </para>
    /// <para>
    /// Here rather than in the shell because this section owns the store access for series, in the same way it
    /// owns the detail fetch. The order itself is <see cref="EpisodeSequence"/>'s, in Core, because it is a
    /// fact about a series and not about this window.
    /// </para>
    /// <para>
    /// Answers only within the selected source. Switching subscription does not stop what is playing, so an
    /// episode of the source just left is still what next refers to — and an address built from its identifier
    /// against the newly selected account is one that cannot work and would be reported as a dead stream.
    /// </para>
    /// </remarks>
    public async Task<EpisodeItemViewModel?> FindAdjacentEpisodeAsync(
        int episodeId,
        int offset,
        CancellationToken cancellationToken)
    {
        var series = await Catalogue.GetSeriesForEpisodeAsync(episodeId, cancellationToken).ConfigureAwait(true);

        if (series is null
            || series.SourceId != Source?.Id
            || EpisodeSequence.Neighbour(series, episodeId, offset) is not { } found)
        {
            return null;
        }

        return new EpisodeItemViewModel(found.Episode, found.SeasonNumber, series.Name);
    }

    /// <summary>
    /// Goes back to the series list from an open series.
    /// </summary>
    /// <remarks>
    /// The two do not share the pane. Shown together, the series list was reduced to two visible rows by an
    /// episode list below it — and browsing a catalogue of eleven thousand series through a two-row window
    /// is not browsing. One replaces the other, and this is the way back.
    /// </remarks>
    [RelayCommand]
    private void CloseSeries()
    {
        // Clearing the selection is what closes it: the shell answers the change by reloading nothing.
        SelectedSeries = null;
    }

    protected override async Task<CataloguePage<SeriesItemViewModel>> SearchAsync(
        int sourceId,
        CatalogueFilter filter,
        CancellationToken cancellationToken)
    {
        var page = await Catalogue.SearchSeriesAsync(sourceId, filter, ResultLimit, cancellationToken)
            .ConfigureAwait(true);

        return new CataloguePage<SeriesItemViewModel>(
            [.. page.Items.Select(series => new SeriesItemViewModel(series))],
            page.TotalMatching);
    }

    protected override bool SupportsSection(PlaylistSource source)
    {
        return source.Capabilities.SupportsSeries;
    }

    protected override void ClearSelection()
    {
        SelectedSeries = null;
        OpenSeries = null;
        SelectedSeason = null;
        SelectedEpisode = null;
        Seasons.Clear();
        Episodes.Clear();
    }

    /// <summary>
    /// Rebuilds the season picker, keeping the season the viewer was looking at.
    /// </summary>
    /// <remarks>
    /// Reselecting by number rather than by reference matters: the choices are new objects after a reload, and
    /// a picker that fell back to season one would move the episode rows out from under the viewer every time
    /// a position was recorded.
    /// </remarks>
    private void ShowSeasons(Series series)
    {
        var seasonNumber = SelectedSeason?.Number;

        OpenSeries = series;
        Seasons.Clear();

        foreach (var season in series.Seasons)
        {
            Seasons.Add(new SeasonChoice(season));
        }

        SelectedSeason = Seasons.FirstOrDefault(season => season.Number == seasonNumber)
            ?? Seasons.FirstOrDefault();
    }

    partial void OnSelectedSeasonChanged(SeasonChoice? value)
    {
        // Cleared first: the rows are about to be replaced, and a selection pointing at a row from another
        // season would have the play command act on an episode that is no longer on screen.
        SelectedEpisode = null;
        Episodes.Clear();

        if (value is null)
        {
            return;
        }

        foreach (var episode in value.Season.Episodes)
        {
            Episodes.Add(new EpisodeItemViewModel(episode, value.Number, OpenSeries?.Name));
        }
    }
}
