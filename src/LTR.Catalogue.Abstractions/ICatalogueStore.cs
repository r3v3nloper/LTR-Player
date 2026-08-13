using LTR.Core.Content;
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

    Task<IReadOnlyList<Category>> GetLiveCategoriesAsync(int sourceId, CancellationToken cancellationToken);

    Task SetFavoriteAsync(int channelId, bool isFavorite, CancellationToken cancellationToken);

    Task DeleteSourceAsync(int sourceId, CancellationToken cancellationToken);
}
