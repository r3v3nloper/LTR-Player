using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Catalogue;

/// <summary>
/// Produces a film or series with everything known about it, fetching from the provider only when the
/// stored copy will not do.
/// </summary>
/// <remarks>
/// <para>
/// Exists because "open this series" is a decision, not a read. A subscription lists thousands of series
/// and each one's seasons take a call of their own, so an import cannot fetch them — but a series opened
/// twice must not be fetched twice either, and one that gained an episode last night must not be served
/// from yesterday's copy. That judgement is application logic: the store knows what it holds and the
/// provider knows what the panel has, and neither can decide on its own.
/// </para>
/// <para>
/// Both methods degrade rather than fail. A panel that cannot be reached leaves the stored copy showing,
/// because a series with last week's episode list is far better than an error page.
/// </para>
/// </remarks>
public interface IVodDetailService
{
    /// <summary>
    /// A series with its seasons and episodes in order, or <see langword="null"/> when it is no longer
    /// in the catalogue.
    /// </summary>
    Task<Series?> GetSeriesAsync(PlaylistSource source, int seriesId, CancellationToken cancellationToken);

    /// <summary>
    /// A film with its synopsis and running time, or <see langword="null"/> when it is no longer in the
    /// catalogue.
    /// </summary>
    Task<VodItem?> GetMovieAsync(PlaylistSource source, int movieId, CancellationToken cancellationToken);
}
