using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Persistence;

namespace LTR.Catalogue;

/// <summary>
/// Reads and writes the local catalogue, one unit of work per call.
/// </summary>
/// <remarks>
/// Holds no context of its own. A <see cref="LtrDbContext"/> is a unit of work meant to be used briefly
/// and discarded (CLAUDE.md §3.3.2), and keeping one alive behind a long-lived service would turn it into
/// a cache with a stale change tracker. This way callers need neither a scope nor a context.
/// </remarks>
internal sealed class CatalogueStore
    : ISourceStore, ILiveCatalogue, IGuideCatalogue, IVodCatalogue, IWatchProgressStore
{
    private readonly CatalogueUnitOfWork _database;
    private readonly TimeProvider _timeProvider;

    /// <param name="timeProvider">
    /// Stamps the moment a film or episode was last watched. Taken here rather than from the caller: the
    /// caller is a view model reacting to playback stopping, and "when did that happen" is not something
    /// it should be able to get wrong.
    /// </param>
    public CatalogueStore(CatalogueUnitOfWork database, TimeProvider timeProvider)
    {
        _database = database;
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<PlaylistSource>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetSourcesAsync(cancellationToken));
    }

    public Task<IReadOnlyList<Channel>> GetLiveChannelsAsync(int sourceId, CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetLiveChannelsAsync(sourceId, cancellationToken));
    }

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(
        int sourceId,
        ContentKind kind,
        CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetCategoriesAsync(sourceId, kind, cancellationToken));
    }

    public Task SetCategoryFavoriteAsync(int categoryId, bool isFavorite, CancellationToken cancellationToken)
    {
        return _database.RunAsync(
            context => context.SetCategoryFavoriteAsync(categoryId, isFavorite, cancellationToken));
    }

    public Task<IReadOnlyList<ChannelGuideSlice>> GetNowAndNextAsync(
        int sourceId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetNowAndNextAsync(sourceId, atUtc, cancellationToken));
    }

    public Task<IReadOnlyDictionary<int, int>> GetGuideLinksAsync(
        int sourceId,
        CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetGuideLinksAsync(sourceId, cancellationToken));
    }

    public Task<IReadOnlyList<EpgEntry>> GetGuideProgrammesAsync(
        IReadOnlyCollection<int> guideChannelIds,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        return _database.RunAsync(context =>
            context.GetGuideProgrammesAsync(guideChannelIds, fromUtc, toUtc, cancellationToken));
    }

    public Task<GuideSummary> GetGuideSummaryAsync(int sourceId, CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetGuideSummaryAsync(sourceId, cancellationToken));
    }

    public Task SetFavoriteAsync(int channelId, bool isFavorite, CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.SetFavoriteAsync(channelId, isFavorite, cancellationToken));
    }

    public Task UpdateSourceSettingsAsync(
        int sourceId,
        string userAgent,
        StreamFormat preferredStreamFormat,
        CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.UpdateSourceSettingsAsync(
            sourceId,
            userAgent,
            preferredStreamFormat,
            cancellationToken));
    }

    public Task DeleteSourceAsync(int sourceId, CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.DeleteSourceAsync(sourceId, cancellationToken));
    }

    public Task<IReadOnlyList<VodItem>> GetMoviesAsync(int sourceId, CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetMoviesAsync(sourceId, cancellationToken));
    }

    public Task<IReadOnlyList<Series>> GetSeriesAsync(int sourceId, CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetSeriesAsync(sourceId, cancellationToken));
    }

    public Task<CataloguePage<VodItem>> SearchMoviesAsync(
        int sourceId,
        CatalogueFilter filter,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return _database.RunAsync(context => context.SearchMoviesAsync(
            sourceId,
            filter.SearchText,
            filter.CategoryExternalId,
            limit,
            cancellationToken));
    }

    public Task<CataloguePage<Series>> SearchSeriesAsync(
        int sourceId,
        CatalogueFilter filter,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return _database.RunAsync(context => context.SearchSeriesAsync(
            sourceId,
            filter.SearchText,
            filter.CategoryExternalId,
            limit,
            cancellationToken));
    }

    public Task<VodItem?> GetMovieAsync(int movieId, CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetMovieAsync(movieId, cancellationToken));
    }

    public Task<Episode?> GetEpisodeAsync(int episodeId, CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetEpisodeAsync(episodeId, cancellationToken));
    }

    public Task<IReadOnlyList<ContinueWatchingEntry>> GetContinueWatchingAsync(
        int sourceId,
        int limit,
        CancellationToken cancellationToken)
    {
        return _database.RunAsync(context =>
            context.GetContinueWatchingAsync(sourceId, limit, cancellationToken));
    }

    public Task RecordMovieProgressAsync(
        int movieId,
        WatchOutcome outcome,
        TimeSpan position,
        CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.RecordMovieProgressAsync(
            movieId,
            outcome,
            position,
            _timeProvider.GetUtcNow(),
            cancellationToken));
    }

    public Task RecordEpisodeProgressAsync(
        int episodeId,
        WatchOutcome outcome,
        TimeSpan position,
        CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.RecordEpisodeProgressAsync(
            episodeId,
            outcome,
            position,
            _timeProvider.GetUtcNow(),
            cancellationToken));
    }

    /// <remarks>
    /// Takes no instant, unlike the two above, which is the difference the method exists for: nothing was
    /// watched, so there is no moment to record.
    /// </remarks>
    public Task ForgetMovieProgressAsync(int movieId, CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.ForgetMovieProgressAsync(movieId, cancellationToken));
    }

    public Task ForgetEpisodeProgressAsync(int episodeId, CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.ForgetEpisodeProgressAsync(episodeId, cancellationToken));
    }
}
