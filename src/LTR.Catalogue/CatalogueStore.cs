using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace LTR.Catalogue;

/// <summary>
/// Reads and writes the local catalogue, one unit of work per call.
/// </summary>
/// <remarks>
/// Creates its own scope per operation rather than holding a context. A <see cref="LtrDbContext"/> is a
/// unit of work meant to be used briefly and discarded (CLAUDE.md §3.3.2), and keeping one alive behind
/// a long-lived service would turn it into a cache with a stale change tracker. This way callers need
/// neither a scope nor a context.
/// </remarks>
internal sealed class CatalogueStore : ICatalogueStore
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CatalogueStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public Task<IReadOnlyList<PlaylistSource>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        return WithContextAsync(context => context.GetSourcesAsync(cancellationToken));
    }

    public Task<IReadOnlyList<Channel>> GetLiveChannelsAsync(int sourceId, CancellationToken cancellationToken)
    {
        return WithContextAsync(context => context.GetLiveChannelsAsync(sourceId, cancellationToken));
    }

    public Task<IReadOnlyList<Category>> GetLiveCategoriesAsync(int sourceId, CancellationToken cancellationToken)
    {
        return WithContextAsync(context => context.GetLiveCategoriesAsync(sourceId, cancellationToken));
    }

    public Task SetFavoriteAsync(int channelId, bool isFavorite, CancellationToken cancellationToken)
    {
        return WithContextAsync(context => context.SetFavoriteAsync(channelId, isFavorite, cancellationToken));
    }

    public Task DeleteSourceAsync(int sourceId, CancellationToken cancellationToken)
    {
        return WithContextAsync(context => context.DeleteSourceAsync(sourceId, cancellationToken));
    }

    private async Task<T> WithContextAsync<T>(Func<LtrDbContext, Task<T>> operation)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();
        return await operation(context).ConfigureAwait(false);
    }

    private async Task WithContextAsync(Func<LtrDbContext, Task> operation)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();
        await operation(context).ConfigureAwait(false);
    }
}
