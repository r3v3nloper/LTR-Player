using CommunityToolkit.Mvvm.ComponentModel;
using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Presents one source's films, and the detail of the one that is selected.
/// </summary>
/// <remarks>
/// The search, the category picker and the count all come from
/// <see cref="CatalogueSectionViewModel{TRow}"/>. What is particular to films is here: selecting one fetches
/// its detail, which is a network call — that is why selection is asynchronous here and instant in the
/// channel list.
/// </remarks>
public sealed partial class MovieListViewModel : CatalogueSectionViewModel<MovieItemViewModel>
{
    private readonly IVodDetailService _detail;
    private readonly ILogger<MovieListViewModel> _logger;

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
        : base(catalogue)
    {
        _detail = detail;
        _logger = logger;
    }

    /// <summary>
    /// The films currently shown. An alias for the base collection, kept because "Movies.Movies" is what the
    /// view binds and reads better there than "Movies.Rows".
    /// </summary>
    public IReadOnlyList<MovieItemViewModel> Movies => Rows;

    protected override ContentKind CategoryKind => ContentKind.Movie;

    protected override string EntryNoun => "films";

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
        if (Source is not { } source || SelectedMovie is not { } selected)
        {
            DetailedMovie = null;
            return;
        }

        // Shown straight away, so the pane is never blank while the panel is being asked.
        DetailedMovie = selected;

        try
        {
            var detailed = await _detail
                .GetMovieAsync(source, selected.Id, cancellationToken)
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

        var refreshed = await Catalogue.GetMovieAsync(SelectedMovie.Id, cancellationToken)
            .ConfigureAwait(true);

        if (refreshed is not null && SelectedMovie?.Id == refreshed.Id)
        {
            DetailedMovie = new MovieItemViewModel(refreshed);
        }
    }

    protected override async Task<CataloguePage<MovieItemViewModel>> SearchAsync(
        int sourceId,
        CatalogueFilter filter,
        CancellationToken cancellationToken)
    {
        var page = await Catalogue.SearchMoviesAsync(sourceId, filter, ResultLimit, cancellationToken)
            .ConfigureAwait(true);

        return new CataloguePage<MovieItemViewModel>(
            [.. page.Items.Select(movie => new MovieItemViewModel(movie))],
            page.TotalMatching);
    }

    protected override bool SupportsSection(PlaylistSource source)
    {
        return source.Capabilities.SupportsVod;
    }

    protected override void ClearSelection()
    {
        SelectedMovie = null;
        DetailedMovie = null;
    }
}
