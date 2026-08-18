using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Catalogue;

/// <summary>
/// The configured subscriptions, and what each of them declares.
/// </summary>
/// <remarks>
/// <para>
/// One of five faces of the stored catalogue. They were a single nineteen-member interface until the review
/// after M6 — everything above this line needed a database it must not know about, and one interface was the
/// shortest way to say so. What that cost was stated by every consumer: the progress recorder declared
/// nineteen members to use three, and any test double had to implement the lot (§2.5).
/// </para>
/// <para>
/// Each method is still self-contained: implementations manage their own unit of work, so callers never hold
/// one open.
/// </para>
/// </remarks>
public interface ISourceStore
{
    Task<IReadOnlyList<PlaylistSource>> GetSourcesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// A source's categories of one kind, in the order the provider intended.
    /// </summary>
    /// <remarks>
    /// Taken by kind rather than one method per kind, which is also why it belongs here rather than on the
    /// live or film catalogue: a panel numbers its categories per section, so the kind is part of the question
    /// and not a variation on it. A method deliberately indifferent to the section belongs with the source.
    /// </remarks>
    Task<IReadOnlyList<Category>> GetCategoriesAsync(
        int sourceId,
        ContentKind kind,
        CancellationToken cancellationToken);

    /// <summary>
    /// Pins a category to the top of the pickers, or lets it fall back into the provider's order.
    /// </summary>
    /// <remarks>
    /// Belongs here for the reason the question above does, and answers it: where a category sits is part of
    /// what the source declares, corrected by what the viewer has said about it.
    /// </remarks>
    Task SetCategoryFavoriteAsync(int categoryId, bool isFavorite, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the two settings a viewer may need to change on a source they have already added.
    /// </summary>
    /// <remarks>
    /// Both exist because panels differ in ways that cannot be probed. A panel that rejects the default
    /// VLC-like agent serves nothing at all, and one whose <c>allowed_output_formats</c> under-reports needs
    /// the container chosen by hand — before this there was no way to correct either without the command
    /// line, which is not much of a remedy for someone whose channels have simply stopped working.
    /// </remarks>
    Task UpdateSourceSettingsAsync(
        int sourceId,
        string userAgent,
        StreamFormat preferredStreamFormat,
        CancellationToken cancellationToken);

    Task DeleteSourceAsync(int sourceId, CancellationToken cancellationToken);
}
