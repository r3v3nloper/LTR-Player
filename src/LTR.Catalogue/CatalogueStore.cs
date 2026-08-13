using LTR.Core.Content;
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
internal sealed class CatalogueStore : ICatalogueStore
{
    private readonly CatalogueUnitOfWork _database;

    public CatalogueStore(CatalogueUnitOfWork database)
    {
        _database = database;
    }

    public Task<IReadOnlyList<PlaylistSource>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetSourcesAsync(cancellationToken));
    }

    public Task<IReadOnlyList<Channel>> GetLiveChannelsAsync(int sourceId, CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetLiveChannelsAsync(sourceId, cancellationToken));
    }

    public Task<IReadOnlyList<Category>> GetLiveCategoriesAsync(int sourceId, CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetLiveCategoriesAsync(sourceId, cancellationToken));
    }

    public Task<IReadOnlyList<ChannelGuideSlice>> GetNowAndNextAsync(
        int sourceId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.GetNowAndNextAsync(sourceId, atUtc, cancellationToken));
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

    public Task DeleteSourceAsync(int sourceId, CancellationToken cancellationToken)
    {
        return _database.RunAsync(context => context.DeleteSourceAsync(sourceId, cancellationToken));
    }
}
