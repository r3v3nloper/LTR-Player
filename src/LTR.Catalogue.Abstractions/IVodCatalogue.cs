using LTR.Core.Content;

namespace LTR.Catalogue;

/// <summary>
/// The stored films and series.
/// </summary>
/// <remarks>
/// Where the viewer got to in one of them is <see cref="IWatchProgressStore"/>'s, not this one's. The split is
/// not tidiness: a refresh owns everything here and must never touch a resume position, which is the same
/// division the reconciliation itself draws.
/// </remarks>
public interface IVodCatalogue
{
    Task<IReadOnlyList<VodItem>> GetMoviesAsync(int sourceId, CancellationToken cancellationToken);

    /// <summary>
    /// Answers a search over a source's films, bounded by <paramref name="limit"/>.
    /// </summary>
    /// <remarks>
    /// What the film list uses, rather than <see cref="GetMoviesAsync"/>. A real subscription holds tens of
    /// thousands of films, so the section answers a search instead of presenting everything — and the count
    /// of what matched comes back with the page so the caller can say how much it is not showing.
    /// </remarks>
    Task<CataloguePage<VodItem>> SearchMoviesAsync(
        int sourceId,
        CatalogueFilter filter,
        int limit,
        CancellationToken cancellationToken);

    Task<CataloguePage<Series>> SearchSeriesAsync(
        int sourceId,
        CatalogueFilter filter,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// A source's series, without their seasons.
    /// </summary>
    /// <remarks>
    /// Shallow because that is how they are stored: seasons arrive from a separate call made when a series
    /// is opened. <see cref="IVodDetailService"/> is what produces a series with its seasons.
    /// </remarks>
    Task<IReadOnlyList<Series>> GetSeriesAsync(int sourceId, CancellationToken cancellationToken);

    Task<VodItem?> GetMovieAsync(int movieId, CancellationToken cancellationToken);

    /// <summary>
    /// One episode on its own, which is what resuming a continue-watching row needs.
    /// </summary>
    /// <remarks>
    /// An episode's address is built from its own identifier, so nothing about its series or season has to
    /// be loaded to play it.
    /// </remarks>
    Task<Episode?> GetEpisodeAsync(int episodeId, CancellationToken cancellationToken);
}
