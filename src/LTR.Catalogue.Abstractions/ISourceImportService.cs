using LTR.Core.Sources;

namespace LTR.Catalogue;

/// <summary>
/// Fetches a source's catalogue and stores it.
/// </summary>
/// <remarks>
/// The sequence — authenticate, probe capabilities, fetch, reconcile — previously existed three times
/// over: adding a source in the window, refreshing one, and adding a playlist from the command line.
/// It is core behaviour and belongs below the user interface, where it is reachable without one
/// (CLAUDE.md §2.12) and where its ordering is stated once.
/// </remarks>
public interface ISourceImportService
{
    /// <summary>
    /// Imports a source that is not yet stored, and stores it when its account proves usable.
    /// </summary>
    /// <param name="source">
    /// A source that has not been persisted. Its capabilities are filled in from the probe and it gains
    /// an identity when stored.
    /// </param>
    Task<SourceImportResult> ImportAsync(
        PlaylistSource source,
        IProgress<SourceImportStage>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-fetches the catalogue of a source that is already stored.
    /// </summary>
    /// <remarks>
    /// Goes through the same reconciliation as an import, which is what preserves the user's favourites
    /// while everything the provider owns is overwritten.
    /// </remarks>
    Task<SourceImportResult> RefreshAsync(
        PlaylistSource source,
        IProgress<SourceImportStage>? progress,
        CancellationToken cancellationToken);
}
