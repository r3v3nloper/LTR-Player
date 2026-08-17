using LTR.Core.Content;

namespace LTR.Catalogue;

/// <summary>
/// The stored programme guide: what is on now, and what a timeline draws.
/// </summary>
public interface IGuideCatalogue
{
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
}
