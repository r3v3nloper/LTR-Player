using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Presents one source's films: a search, a category picker and the film that is selected.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the channel list, this one does not hold the catalogue. A real subscription's film section runs
/// to tens of thousands of entries — sixty-six thousand for the one this was built against — and nobody
/// browses that by scrolling, so the section answers a search and says how much it is not showing.
/// </para>
/// <para>
/// Selecting a film fetches its detail, which is a network call. That is why selection is asynchronous
/// here and instant in the channel list.
/// </para>
/// </remarks>
public sealed partial class MovieListViewModel : ObservableObject
{
    /// <summary>
    /// How many results a search shows.
    /// </summary>
    /// <remarks>
    /// Enough that a search for a title lands it on screen, small enough that the list stays instant. What
    /// is left out is stated rather than silently dropped.
    /// </remarks>
    public const int ResultLimit = 200;

    private readonly ICatalogueStore _catalogue;
    private readonly IVodDetailService _detail;
    private readonly ILogger<MovieListViewModel> _logger;

    private PlaylistSource? _source;

    [ObservableProperty]
    private CategoryChoice _selectedCategory = CategoryChoice.All;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>What the section is showing, or why it is showing nothing.</summary>
    [ObservableProperty]
    private string _notice = string.Empty;

    [ObservableProperty]
    private MovieItemViewModel? _selectedMovie;

    /// <summary>
    /// The selected film with everything known about it, once its detail has been read.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SelectedMovie"/> because the detail arrives later than the selection. The
    /// row appears immediately and the synopsis fills in when the panel answers.
    /// </remarks>
    [ObservableProperty]
    private MovieItemViewModel? _detailedMovie;

    public MovieListViewModel(
        ICatalogueStore catalogue,
        IVodDetailService detail,
        ILogger<MovieListViewModel> logger)
    {
        _catalogue = catalogue;
        _detail = detail;
        _logger = logger;
    }

    public ObservableCollection<CategoryChoice> Categories { get; } = [CategoryChoice.All];

    public ObservableCollection<MovieItemViewModel> Movies { get; } = [];

    /// <summary>Whether the source offers films at all, which decides whether the section is shown.</summary>
    public bool IsAvailable => _source?.Capabilities.SupportsVod ?? false;

    /// <summary>
    /// Points the section at a source, loading its categories and a first page of results.
    /// </summary>
    public async Task ShowAsync(PlaylistSource? source, CancellationToken cancellationToken)
    {
        // Detached first, and that ordering is deliberate: clearing the criteria below raises the property
        // changes the shell answers with a search, and a section with no source answers nothing. Otherwise
        // switching subscriptions would run three searches to display one.
        _source = null;

        SelectedMovie = null;
        DetailedMovie = null;
        SelectedCategory = CategoryChoice.All;
        SearchText = string.Empty;

        Movies.Clear();
        Categories.Clear();
        Categories.Add(CategoryChoice.All);

        _source = source;
        OnPropertyChanged(nameof(IsAvailable));

        if (source is null)
        {
            Notice = string.Empty;
            return;
        }

        var categories = await _catalogue
            .GetCategoriesAsync(source.Id, ContentKind.Movie, cancellationToken)
            .ConfigureAwait(true);

        foreach (var category in categories)
        {
            Categories.Add(new CategoryChoice(category.Name, category.ExternalId));
        }

        await SearchAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Reruns the search with the current criteria.
    /// </summary>
    public async Task SearchAsync(CancellationToken cancellationToken)
    {
        if (_source is null)
        {
            return;
        }

        var filter = new CatalogueFilter(SearchText, SelectedCategory?.ExternalId);

        var page = await _catalogue
            .SearchMoviesAsync(_source.Id, filter, ResultLimit, cancellationToken)
            .ConfigureAwait(true);

        Movies.Clear();

        foreach (var movie in page.Items)
        {
            Movies.Add(new MovieItemViewModel(movie));
        }

        Notice = Describe(page, filter);
    }

    /// <summary>
    /// Reads the selected film's detail, so the synopsis and the container extension are known.
    /// </summary>
    /// <remarks>
    /// Called by the shell rather than from the property setter, because it awaits a network call and a
    /// property setter cannot. The result is discarded when the selection has moved on in the meantime,
    /// which is what stops a slow answer overwriting a newer one.
    /// </remarks>
    public async Task LoadSelectedDetailAsync(CancellationToken cancellationToken)
    {
        if (_source is null || SelectedMovie is not { } selected)
        {
            DetailedMovie = null;
            return;
        }

        // Shown straight away, so the pane is never blank while the panel is being asked.
        DetailedMovie = selected;

        try
        {
            var detailed = await _detail
                .GetMovieAsync(_source, selected.Id, cancellationToken)
                .ConfigureAwait(true);

            if (detailed is not null && SelectedMovie?.Id == selected.Id)
            {
                DetailedMovie = new MovieItemViewModel(detailed);
            }
        }
        catch (OperationCanceledException)
        {
            // The selection moved on, or the window is closing.
        }
        catch (Exception exception)
        {
            // The detail is a nicety; the film still plays without it.
            PlayerLog.MovieDetailFailed(_logger, exception, selected.Name);
        }
    }

    /// <summary>
    /// Rereads the selected film, which is how a position recorded during playback reaches the screen.
    /// </summary>
    public async Task RefreshSelectedAsync(CancellationToken cancellationToken)
    {
        if (SelectedMovie is null)
        {
            return;
        }

        var refreshed = await _catalogue.GetMovieAsync(SelectedMovie.Id, cancellationToken)
            .ConfigureAwait(true);

        if (refreshed is not null && SelectedMovie?.Id == refreshed.Id)
        {
            DetailedMovie = new MovieItemViewModel(refreshed);
        }
    }

    private static string Describe(CataloguePage<VodItem> page, CatalogueFilter filter)
    {
        if (page.TotalMatching == 0)
        {
            return filter.IsActive
                ? "No films match. Try fewer words, or another category."
                : "This subscription's film catalogue is empty. Refresh the source to fetch it.";
        }

        return page.IsTruncated
            ? $"Showing {page.Items.Count} of {page.TotalMatching} films. Narrow the search to see the rest."
            : $"{page.TotalMatching} films.";
    }
}
