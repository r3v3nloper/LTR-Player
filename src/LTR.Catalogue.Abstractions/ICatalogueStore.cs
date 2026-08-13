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
}
