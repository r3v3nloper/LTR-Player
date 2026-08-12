using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Providers;

/// <summary>
/// Talks to one configured source and returns its catalogue.
/// </summary>
/// <remarks>
/// An instance is bound to a single <see cref="PlaylistSource"/>, so returned entities already
/// carry the correct <c>SourceId</c>. They are otherwise unsaved: identifiers stay zero until the
/// persistence layer reconciles them against what is already stored.
/// </remarks>
public interface IContentProvider
{
    PlaylistSource Source { get; }

    /// <summary>
    /// Verifies the credentials and reports the subscription's live state, including the
    /// connection limit that governs how many streams may be opened.
    /// </summary>
    Task<ProviderAccount> AuthenticateAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Category>> FetchCategoriesAsync(ContentKind kind, CancellationToken cancellationToken);

    Task<IReadOnlyList<Channel>> FetchLiveChannelsAsync(CancellationToken cancellationToken);
}
