using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Presents one source's series, and the seasons and episodes of the one that is selected.
/// </summary>
/// <remarks>
/// Selecting a series is the expensive act here: its seasons are not part of an import and have to be
/// fetched from the panel the first time, which is why <see cref="LoadSelectedAsync"/> exists and is
/// awaited by the shell rather than run from a property setter.
/// </remarks>
public sealed partial class SeriesCatalogueViewModel : ObservableObject
{
    /// <summary>How many results a search shows, for the same reason as in the film list.</summary>
    public const int ResultLimit = 200;

    private readonly ICatalogueStore _catalogue;
    private readonly IVodDetailService _detail;
    private readonly ILogger<SeriesCatalogueViewModel> _logger;

    private PlaylistSource? _source;

    [ObservableProperty]
    private CategoryChoice _selectedCategory = CategoryChoice.All;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _notice = string.Empty;

    [ObservableProperty]
    private SeriesItemViewModel? _selectedSeries;

    /// <summary>The selected series once its seasons are known, or null while none is selected.</summary>
    [ObservableProperty]
    private Series? _openSeries;

    [ObservableProperty]
    private SeasonChoice? _selectedSeason;

    /// <summary>Whether a season's episodes are still being fetched, so the view can say so.</summary>
    [ObservableProperty]
    private bool _isLoadingEpisodes;

    public SeriesCatalogueViewModel(
        ICatalogueStore catalogue,
        IVodDetailService detail,
        ILogger<SeriesCatalogueViewModel> logger)
    {
        _catalogue = catalogue;
        _detail = detail;
        _logger = logger;
    }

    public ObservableCollection<CategoryChoice> Categories { get; } = [CategoryChoice.All];

    public ObservableCollection<SeriesItemViewModel> Series { get; } = [];

    public ObservableCollection<SeasonChoice> Seasons { get; } = [];

    public ObservableCollection<EpisodeItemViewModel> Episodes { get; } = [];

    public bool IsAvailable => _source?.Capabilities.SupportsSeries ?? false;

    public async Task ShowAsync(PlaylistSource? source, CancellationToken cancellationToken)
    {
        // Detached first, for the same reason as in the film list: the resets below raise property changes
        // the shell answers with a search, and a section with no source answers nothing.
        _source = null;

        ClearSelection();
        SearchText = string.Empty;

        Series.Clear();
        Categories.Clear();
        Categories.Add(CategoryChoice.All);

        if (source is null)
        {
            SelectedCategory = CategoryChoice.All;
            Notice = string.Empty;
            OnPropertyChanged(nameof(IsAvailable));
            return;
        }

        var categories = await _catalogue
            .GetCategoriesAsync(source.Id, ContentKind.Series, cancellationToken)
            .ConfigureAwait(true);

        foreach (var category in categories)
        {
            Categories.Add(new CategoryChoice(category.Name, category.ExternalId));
        }

        // Selected only once the picker is complete, for the reason recorded in the film list: emptying the
        // bound collection makes the ComboBox write a null selection back, so selecting before refilling
        // leaves the control blank while the list itself looks perfectly correct.
        SelectedCategory = CategoryChoice.All;

        _source = source;
        OnPropertyChanged(nameof(IsAvailable));

        await SearchAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task SearchAsync(CancellationToken cancellationToken)
    {
        if (_source is null)
        {
            return;
        }

        var filter = new CatalogueFilter(SearchText, SelectedCategory?.ExternalId);

        var page = await _catalogue
            .SearchSeriesAsync(_source.Id, filter, ResultLimit, cancellationToken)
            .ConfigureAwait(true);

        Series.Clear();

        foreach (var series in page.Items)
        {
            Series.Add(new SeriesItemViewModel(series));
        }

        Notice = Describe(page, filter);
    }

    /// <summary>
    /// Opens the selected series, fetching its seasons when the stored copy will not do.
    /// </summary>
    public async Task LoadSelectedAsync(CancellationToken cancellationToken)
    {
        if (_source is null || SelectedSeries is not { } selected)
        {
            ClearSelection();
            return;
        }

        IsLoadingEpisodes = true;

        try
        {
            var series = await _detail
                .GetSeriesAsync(_source, selected.Id, cancellationToken)
                .ConfigureAwait(true);

            // Discarded when the selection moved on while the panel was being asked, which is what stops a
            // slow answer replacing the episodes of a series the viewer has already left.
            if (series is null || SelectedSeries?.Id != selected.Id)
            {
                return;
            }

            OpenSeries = series;
            Seasons.Clear();

            foreach (var season in series.Seasons)
            {
                Seasons.Add(new SeasonChoice(season));
            }

            SelectedSeason = Seasons.FirstOrDefault();

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
        if (_source is null || OpenSeries is not { } open)
        {
            return;
        }

        // Goes through the detail service rather than reaching for the store, and costs nothing extra: the
        // stored detail is current by definition here, so this is a read rather than a fetch.
        var reloaded = await _detail.GetSeriesAsync(_source, open.Id, cancellationToken).ConfigureAwait(true);

        if (reloaded is null || OpenSeries?.Id != reloaded.Id)
        {
            return;
        }

        // The season is reselected by number so that the episode rows do not jump back to season one when a
        // position is recorded.
        var seasonNumber = SelectedSeason?.Number;
        OpenSeries = reloaded;
        Seasons.Clear();

        foreach (var season in reloaded.Seasons)
        {
            Seasons.Add(new SeasonChoice(season));
        }

        SelectedSeason = Seasons.FirstOrDefault(season => season.Number == seasonNumber)
            ?? Seasons.FirstOrDefault();
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

    partial void OnSelectedSeasonChanged(SeasonChoice? value)
    {
        Episodes.Clear();

        if (value is null)
        {
            return;
        }

        foreach (var episode in value.Season.Episodes)
        {
            Episodes.Add(new EpisodeItemViewModel(episode, value.Number));
        }
    }

    private void ClearSelection()
    {
        SelectedSeries = null;
        OpenSeries = null;
        SelectedSeason = null;
        Seasons.Clear();
        Episodes.Clear();
    }

    private static string Describe(CataloguePage<Series> page, CatalogueFilter filter)
    {
        if (page.TotalMatching == 0)
        {
            return filter.IsActive
                ? "No series match. Try fewer words, or another category."
                : "This subscription's series catalogue is empty. Refresh the source to fetch it.";
        }

        return page.IsTruncated
            ? $"Showing {page.Items.Count} of {page.TotalMatching} series. Narrow the search to see the rest."
            : $"{page.TotalMatching} series.";
    }
}
