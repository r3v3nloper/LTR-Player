using LTR.Core.Sources;

namespace LTR.Catalogue;

/// <summary>
/// Imports a source's programme guide and joins it to that source's channels.
/// </summary>
public interface IGuideImportService
{
    /// <summary>
    /// How old a guide may be before an automatic import is considered due.
    /// </summary>
    /// <remarks>
    /// Half a day, because a guide reaches days ahead and a download of this size is not worth repeating
    /// more often than that. Stated here rather than at the call sites so the window and the command line
    /// cannot disagree about it.
    /// </remarks>
    static TimeSpan StaleAfter => TimeSpan.FromHours(12);

    Task<GuideImportResult> ImportAsync(
        PlaylistSource source,
        IProgress<GuideImportStage>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Imports only when the source has no guide or its guide has gone stale.
    /// </summary>
    /// <remarks>
    /// The distinction exists because the two callers want different things: a button means "fetch it
    /// now", whereas following a catalogue refresh means "fetch it if it is worth fetching". Deciding
    /// that here keeps the freshness rule out of both.
    /// </remarks>
    Task<GuideImportResult> ImportIfStaleAsync(
        PlaylistSource source,
        IProgress<GuideImportStage>? progress,
        CancellationToken cancellationToken);
}
