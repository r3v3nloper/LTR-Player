using LTR.Core.Content;

namespace LTR.Catalogue;

/// <summary>
/// What a guide import achieved.
/// </summary>
/// <param name="Outcome">Whether a guide was imported, and if not, why not.</param>
/// <param name="ProgrammeCount">Programmes stored.</param>
/// <param name="MatchedChannelCount">
/// Channels that ended up with a guide. The figure that decides whether the import was worth anything:
/// a guide can read perfectly and still match nothing, if its channel names bear no resemblance to the
/// subscription's.
/// </param>
/// <param name="WasTruncated">Whether the document ended early and what was kept is partial.</param>
/// <param name="Summary">The state of the source's guide afterwards, for reporting.</param>
public sealed record GuideImportResult(
    GuideImportOutcome Outcome,
    int ProgrammeCount,
    int MatchedChannelCount,
    bool WasTruncated,
    GuideSummary? Summary)
{
    public bool Succeeded => Outcome == GuideImportOutcome.Imported;

    /// <summary>
    /// The source turned out to have no guide to import. Not a failure — most M3U playlists and a
    /// minority of panels simply have none.
    /// </summary>
    public static GuideImportResult NoGuideAvailable { get; } =
        new(GuideImportOutcome.NoGuideAvailable, 0, 0, WasTruncated: false, Summary: null);

    /// <summary>
    /// The guide was reachable but contained no usable programme, which in practice means the address
    /// served something that was not a guide.
    /// </summary>
    public static GuideImportResult Empty { get; } =
        new(GuideImportOutcome.Empty, 0, 0, WasTruncated: false, Summary: null);

    /// <summary>The stored guide was still fresh, so nothing was fetched.</summary>
    public static GuideImportResult NotDue { get; } =
        new(GuideImportOutcome.NotDue, 0, 0, WasTruncated: false, Summary: null);
}
