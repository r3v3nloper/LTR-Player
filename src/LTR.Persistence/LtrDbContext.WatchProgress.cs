using LTR.Core.Content;
using LTR.Core.Playback;
using Microsoft.EntityFrameworkCore;

namespace LTR.Persistence;

/// <summary>
/// Where the viewer got to, which is the one part of the catalogue the viewer owns.
/// </summary>
/// <remarks>
/// <para>
/// Its own partial because <c>IWatchProgressStore</c> — named rather than referenced, since this project
/// knows nothing of the application layer that declares it — is its own face over this context, and because
/// the distinction it keeps is the one the film and series half exists to respect: a refresh owns everything
/// a provider states and must never touch a position. Among those queries, these five were the ones a reader
/// had to pick out from the ones that may overwrite freely.
/// </para>
/// <para>
/// Two rules run through all of it. A <see cref="WatchOutcome"/> is translated into columns here and
/// nowhere else, so what "finished" does to a row is decided once. And <c>IsWatched</c> is never unset —
/// reopening a film that was watched through and closing it again is not un-watching it.
/// </para>
/// </remarks>
public sealed partial class LtrDbContext
{

    /// <summary>
    /// Lists what the viewer is part-way through, most recently watched first.
    /// </summary>
    /// <remarks>
    /// Two queries and a merge rather than one union. Films and episodes share no table, and the four
    /// fields each contributes are reached differently — an episode's title and cover come from its series
    /// two joins away. Both sides are limited before the merge, so the transfer is bounded by
    /// <paramref name="limit"/> either way.
    /// </remarks>
    public async Task<IReadOnlyList<ContinueWatchingEntry>> GetContinueWatchingAsync(
        int sourceId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var movies = await Movies
            .AsNoTracking()
            .Where(movie => movie.SourceId == sourceId && movie.ResumePositionSeconds != null)
            .OrderByDescending(movie => movie.LastWatchedUtc)
            .Take(limit)
            .Select(movie => new ContinueWatchingEntry(
                ContentKind.Movie,
                movie.Id,
                movie.Name,
                string.Empty,
                movie.CoverUrl,
                movie.ResumePositionSeconds!.Value,
                movie.DurationSeconds,
                movie.LastWatchedUtc!.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var episodes = await Episodes
            .AsNoTracking()
            .Where(episode => episode.Season!.Series!.SourceId == sourceId
                && episode.ResumePositionSeconds != null)
            .OrderByDescending(episode => episode.LastWatchedUtc)
            .Take(limit)
            .Select(episode => new
            {
                episode.Id,
                SeriesName = episode.Season!.Series!.Name,
                Cover = episode.Season!.Series!.CoverUrl,
                SeasonNumber = episode.Season!.Number,
                episode.Number,
                episode.Title,
                Position = episode.ResumePositionSeconds!.Value,
                episode.DurationSeconds,
                WatchedAt = episode.LastWatchedUtc!.Value,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The label is composed here rather than in the query, because it is string formatting SQLite has
        // no expression for and the row set is already bounded by the limit.
        var entries = episodes.Select(episode => new ContinueWatchingEntry(
            ContentKind.Series,
            episode.Id,
            episode.SeriesName,
            EpisodeNaming.Describe(episode.SeasonNumber, episode.Number, episode.Title),
            episode.Cover,
            episode.Position,
            episode.DurationSeconds,
            episode.WatchedAt));

        return
        [
            .. movies
                .Concat(entries)
                .OrderByDescending(entry => entry.LastWatchedUtc)
                .Take(limit),
        ];
    }

    /// <summary>
    /// Records where the viewer left a film.
    /// </summary>
    /// <remarks>
    /// The <see cref="WatchOutcome"/> is translated into columns here rather than by the caller, so that
    /// only one place decides what "finished" does to a row. Written with an update statement rather than
    /// by loading the film: this runs while playback is stopping, and it must not depend on a catalogue
    /// entity still being in hand.
    /// </remarks>
    public Task RecordMovieProgressAsync(
        int movieId,
        WatchOutcome outcome,
        TimeSpan position,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        var resumeAt = ResumeSecondsFor(outcome, position);
        var finished = outcome == WatchOutcome.Finished;

        return Movies
            .Where(movie => movie.Id == movieId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(movie => movie.ResumePositionSeconds, resumeAt)
                    .SetProperty(movie => movie.LastWatchedUtc, atUtc)

                    // Never unset. Opening a film that was already watched through and closing it again
                    // is not un-watching it.
                    .SetProperty(movie => movie.IsWatched, movie => movie.IsWatched || finished),
                cancellationToken);
    }

    /// <summary>Records where the viewer left an episode.</summary>
    public Task RecordEpisodeProgressAsync(
        int episodeId,
        WatchOutcome outcome,
        TimeSpan position,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        var resumeAt = ResumeSecondsFor(outcome, position);
        var finished = outcome == WatchOutcome.Finished;

        return Episodes
            .Where(episode => episode.Id == episodeId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(episode => episode.ResumePositionSeconds, resumeAt)
                    .SetProperty(episode => episode.LastWatchedUtc, atUtc)
                    .SetProperty(episode => episode.IsWatched, episode => episode.IsWatched || finished),
                cancellationToken);
    }

    /// <summary>
    /// Clears a film's stored position at the viewer's request, and says nothing else about it.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="RecordMovieProgressAsync"/> with a discarding outcome, which is how both
    /// front ends used to do this: that also writes <c>LastWatchedUtc</c>, so removing an entry from the
    /// continue-watching list stamped the row as watched at the moment of removal. <c>IsWatched</c> is left
    /// alone too — forgetting where you got to in a film you had already finished does not unfinish it.
    /// </remarks>
    public Task ForgetMovieProgressAsync(int movieId, CancellationToken cancellationToken)
    {
        return Movies
            .Where(movie => movie.Id == movieId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(movie => movie.ResumePositionSeconds, (int?)null),
                cancellationToken);
    }

    /// <summary>Clears an episode's stored position at the viewer's request.</summary>
    public Task ForgetEpisodeProgressAsync(int episodeId, CancellationToken cancellationToken)
    {
        return Episodes
            .Where(episode => episode.Id == episodeId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(episode => episode.ResumePositionSeconds, (int?)null),
                cancellationToken);
    }

    /// <summary>
    /// The position to store, which only a part-watched item has.
    /// </summary>
    /// <remarks>
    /// Both the discarded and the finished outcomes clear it, for the same reason from opposite ends: an
    /// item that offers to resume at its first minute or at its closing credits is offering nothing.
    /// </remarks>
    private static int? ResumeSecondsFor(WatchOutcome outcome, TimeSpan position)
    {
        return outcome == WatchOutcome.Resumable ? (int)position.TotalSeconds : null;
    }
}
