using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;

namespace LTR.Catalogue;

/// <summary>
/// The locally stored catalogue.
/// </summary>
/// <remarks>
/// Exists so that nothing above this line needs to know a database is involved. Before it, the view
/// model opened dependency injection scopes and issued Entity Framework calls itself — persistence
/// knowledge in the view layer, and the reason the view model could not be tested without a database.
/// Each method is self-contained: implementations manage their own unit of work, so callers never hold
/// one open.
/// </remarks>
public interface ICatalogueStore
{
    Task<IReadOnlyList<PlaylistSource>> GetSourcesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Channel>> GetLiveChannelsAsync(int sourceId, CancellationToken cancellationToken);

    /// <summary>
    /// A source's categories of one kind, in the order the provider intended.
    /// </summary>
    /// <remarks>
    /// Taken by kind rather than one method per kind. A panel numbers its categories per section, so the
    /// kind is part of the question and not a variation on it.
    /// </remarks>
    Task<IReadOnlyList<Category>> GetCategoriesAsync(
        int sourceId,
        ContentKind kind,
        CancellationToken cancellationToken);

    /// <summary>
    /// What is on now and next on every channel of a source that has a guide.
    /// </summary>
    /// <remarks>
    /// Answered for the whole source at once rather than per row. The channel list needs it for every
    /// visible row, and rows come and go as the user scrolls, so asking per row would be thousands of
    /// queries driven by a scroll bar.
    /// </remarks>
    Task<IReadOnlyList<ChannelGuideSlice>> GetNowAndNextAsync(
        int sourceId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Which guide channel each of a source's channels is matched to.
    /// </summary>
    /// <remarks>
    /// Read from the store rather than from a <see cref="Channel"/> already in hand, because the link is
    /// written by a guide import that runs long after the channel list was loaded.
    /// </remarks>
    Task<IReadOnlyDictionary<int, int>> GetGuideLinksAsync(int sourceId, CancellationToken cancellationToken);

    /// <summary>
    /// The programmes of specific guide channels that overlap a time window, which is what a timeline
    /// draws.
    /// </summary>
    Task<IReadOnlyList<EpgEntry>> GetGuideProgrammesAsync(
        IReadOnlyCollection<int> guideChannelIds,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);

    Task<GuideSummary> GetGuideSummaryAsync(int sourceId, CancellationToken cancellationToken);

    Task SetFavoriteAsync(int channelId, bool isFavorite, CancellationToken cancellationToken);

    Task DeleteSourceAsync(int sourceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<VodItem>> GetMoviesAsync(int sourceId, CancellationToken cancellationToken);

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

    /// <summary>What the viewer is part-way through, most recently watched first.</summary>
    Task<IReadOnlyList<ContinueWatchingEntry>> GetContinueWatchingAsync(
        int sourceId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records where the viewer left a film.
    /// </summary>
    /// <remarks>
    /// Takes the outcome rather than a set of column values, so that what "finished" means to a row is
    /// decided in one place. <see cref="LTR.Core.Playback.ResumePolicy"/> is what produces it.
    /// </remarks>
    Task RecordMovieProgressAsync(
        int movieId,
        WatchOutcome outcome,
        TimeSpan position,
        CancellationToken cancellationToken);

    /// <summary>Records where the viewer left an episode.</summary>
    Task RecordEpisodeProgressAsync(
        int episodeId,
        WatchOutcome outcome,
        TimeSpan position,
        CancellationToken cancellationToken);
}
