using LTR.Catalogue;
using LTR.Core.Sources;

namespace LTR.Cli;

/// <summary>
/// Finds a stored source by the id the commands take, and says so plainly when there is none.
/// </summary>
/// <remarks>
/// Every command working against the catalogue starts with this, and the wording of the failure is part of
/// it: an id that does not exist is the most common mistake at this prompt, and the answer has to say where
/// to find the right one.
/// </remarks>
internal sealed class StoredSourceLookup
{
    private readonly ISourceStore _sources;

    public StoredSourceLookup(ISourceStore sources)
    {
        _sources = sources;
    }

    /// <returns>The source, or <see langword="null"/> after reporting that there is no such id.</returns>
    public async Task<PlaylistSource?> FindAsync(int sourceId, CancellationToken cancellationToken)
    {
        var sources = await _sources.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        var source = sources.FirstOrDefault(candidate => candidate.Id == sourceId);

        if (source is null)
        {
            Console.Error.WriteLine($"No source with id {sourceId}. Run 'sources list' to see what there is.");
        }

        return source;
    }
}
