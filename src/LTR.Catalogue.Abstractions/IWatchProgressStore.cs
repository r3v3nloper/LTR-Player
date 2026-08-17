using LTR.Core.Content;
using LTR.Core.Playback;

namespace LTR.Catalogue;

/// <summary>
/// Where the viewer got to in a film or an episode.
/// </summary>
/// <remarks>
/// The clearest case for the split this arrived in: <see cref="WatchProgressRecorder"/> uses three members and
/// declared nineteen, and a test double for it had to answer questions about guide channels to compile.
/// Nothing here applies to live television, which has no position and nothing to resume.
/// </remarks>
public interface IWatchProgressStore
{
    /// <summary>What the viewer is part-way through, most recently watched first.</summary>
    Task<IReadOnlyList<ContinueWatchingEntry>> GetContinueWatchingAsync(
        int sourceId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records where the viewer left a film.
    /// </summary>
    /// <remarks>
    /// Takes the outcome rather than a set of column values, so that what "finished" means to a row is
    /// decided in one place. <see cref="ResumePolicy"/> is what produces it.
    /// </remarks>
    Task RecordMovieProgressAsync(
        int movieId,
        WatchOutcome outcome,
        TimeSpan position,
        CancellationToken cancellationToken);

    /// <summary>Records where the viewer left an episode.</summary>
    Task RecordEpisodeProgressAsync(
        int episodeId,
        WatchOutcome outcome,
        TimeSpan position,
        CancellationToken cancellationToken);
}
