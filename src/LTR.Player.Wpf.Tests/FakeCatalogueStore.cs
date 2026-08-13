using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Player.Wpf;

/// <summary>
/// An in-memory catalogue, so the view model can be exercised without a database.
/// </summary>
internal sealed class FakeCatalogueStore : ICatalogueStore
{
    public List<PlaylistSource> Sources { get; } = [];

    public List<Channel> Channels { get; } = [];

    public List<Category> Categories { get; } = [];

    public List<int> DeletedSourceIds { get; } = [];

    /// <summary>Favourite changes that were written, so a test can prove one was persisted.</summary>
    public List<(int ChannelId, bool IsFavorite)> FavoriteWrites { get; } = [];

    public Task<IReadOnlyList<PlaylistSource>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<PlaylistSource>>(Sources);
    }

    public Task<IReadOnlyList<Channel>> GetLiveChannelsAsync(int sourceId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Channel>>(
            [.. Channels.Where(channel => channel.SourceId == sourceId)]);
    }

    public Task<IReadOnlyList<Category>> GetLiveCategoriesAsync(int sourceId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Category>>(
            [.. Categories.Where(category => category.SourceId == sourceId)]);
    }

    public Task SetFavoriteAsync(int channelId, bool isFavorite, CancellationToken cancellationToken)
    {
        FavoriteWrites.Add((channelId, isFavorite));
        return Task.CompletedTask;
    }

    public Task DeleteSourceAsync(int sourceId, CancellationToken cancellationToken)
    {
        DeletedSourceIds.Add(sourceId);
        Sources.RemoveAll(source => source.Id == sourceId);
        return Task.CompletedTask;
    }
}
