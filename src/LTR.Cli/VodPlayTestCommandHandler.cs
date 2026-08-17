using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Playback;
using LTR.Providers;

namespace LTR.Cli;

/// <summary>
/// Opens a stored film or episode for a few seconds and releases it again.
/// </summary>
/// <remarks>
/// The equivalent of <c>play-test</c> for video on demand, and it verifies two things that command cannot:
/// that the <c>/movie/</c> and <c>/series/</c> address shapes are right for this panel, and that a resume
/// position is honoured — a film asked to start forty minutes in that begins at zero looks perfectly healthy
/// from every other angle. The hold itself is <see cref="StreamHoldTest"/>, shared with the live command.
/// </remarks>
internal sealed class VodPlayTestCommandHandler
{
    private readonly StoredSourceLookup _sources;
    private readonly IVodCatalogue _catalogue;
    private readonly IProviderRegistry _providers;
    private readonly IPlaybackTransport _playback;
    private readonly StreamHoldTest _holdTest;
    private readonly WatchProgressRecorder _progress;

    /// <param name="progress">
    /// The same recorder the window uses. Shared rather than reimplemented, so that "how much counts as
    /// watched" cannot come out differently here than on screen.
    /// </param>
    public VodPlayTestCommandHandler(
        StoredSourceLookup sources,
        IVodCatalogue catalogue,
        IProviderRegistry providers,
        IPlaybackTransport playback,
        StreamHoldTest holdTest,
        WatchProgressRecorder progress)
    {
        _sources = sources;
        _catalogue = catalogue;
        _providers = providers;
        _playback = playback;
        _holdTest = holdTest;
        _progress = progress;
    }

    /// <param name="remember">
    /// Whether to record where playback got to, as the window does. Off by default so that a play-test stays
    /// a read-only check; on, it is what lets the continue-watching list be exercised without the window.
    /// </param>
    /// <param name="seekToSeconds">
    /// Where to seek part-way through the hold, or zero for no seek. Distinct from
    /// <paramref name="startAtSeconds"/>, which is honoured while the stream is opening: that path is the
    /// resume, and this one is what the seek bar does to a stream already playing.
    /// </param>
    public async Task<int> ExecuteAsync(
        int sourceId,
        int? movieId,
        int? episodeId,
        int seconds,
        int startAtSeconds,
        int seekToSeconds,
        bool remember,
        CancellationToken cancellationToken)
    {
        if (await _sources.FindAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
        {
            return 1;
        }

        if ((movieId is null) == (episodeId is null))
        {
            Console.Error.WriteLine("Pass exactly one of --movie-id and --episode-id.");
            return 1;
        }

        var startAt = startAtSeconds > 0 ? TimeSpan.FromSeconds(startAtSeconds) : (TimeSpan?)null;

        var request = movieId is { } film
            ? await ResolveMovieAsync(source, film, startAt, remember, cancellationToken).ConfigureAwait(false)
            : await ResolveEpisodeAsync(source, episodeId!.Value, startAt, remember, cancellationToken)
                .ConfigureAwait(false);

        if (request is null)
        {
            return 1;
        }

        var seekTo = seekToSeconds > 0 ? TimeSpan.FromSeconds(seekToSeconds) : (TimeSpan?)null;

        try
        {
            return await _holdTest
                .RunAsync(
                    source,
                    request,
                    seconds,
                    token => ReportPlaybackAsync(seekTo, seconds, token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // Whatever was being followed is dropped if it was never recorded, so an interrupted run does
            // not leave the recorder holding an item for the next command in the same process.
            _progress.Forget();
        }
    }

    private async Task<MediaRequest?> ResolveMovieAsync(
        PlaylistSource source,
        int movieId,
        TimeSpan? startAt,
        bool remember,
        CancellationToken cancellationToken)
    {
        var movie = await _catalogue.GetMovieAsync(movieId, cancellationToken).ConfigureAwait(false);

        if (movie is null)
        {
            Console.Error.WriteLine($"No film with id {movieId}.");
            return null;
        }

        if (remember)
        {
            // Seeded with the position playback was asked to start at, exactly as the window does: a deep
            // seek can take longer than this command holds the stream, and reading that as "back at the
            // beginning" would throw the viewer's place away.
            _progress.Track(ContentKind.Movie, movie.Id, startAt ?? TimeSpan.Zero);
        }

        return _providers.GetStreamUrlResolver(source).ResolveMovie(source, movie, startAt);
    }

    private async Task<MediaRequest?> ResolveEpisodeAsync(
        PlaylistSource source,
        int episodeId,
        TimeSpan? startAt,
        bool remember,
        CancellationToken cancellationToken)
    {
        var episode = await _catalogue.GetEpisodeAsync(episodeId, cancellationToken).ConfigureAwait(false);

        if (episode is null)
        {
            Console.Error.WriteLine($"No episode with id {episodeId}.");
            return null;
        }

        if (remember)
        {
            _progress.Track(ContentKind.Series, episode.Id, startAt ?? TimeSpan.Zero);
        }

        return _providers.GetStreamUrlResolver(source).ResolveEpisode(source, episode, startAt);
    }

    /// <summary>
    /// Reads what only a playing film can report, and records where it got to.
    /// </summary>
    /// <remarks>
    /// Runs while the stream is still open, because the engine has neither figure afterwards — the same
    /// reason the window samples on a timer rather than at the moment of saving.
    /// </remarks>
    private async Task ReportPlaybackAsync(TimeSpan? seekTo, int seconds, CancellationToken cancellationToken)
    {
        if (seekTo is { } target)
        {
            await SeekAndReportAsync(target, seconds, cancellationToken).ConfigureAwait(false);
        }

        // The two figures that decide whether resuming can work at all. A film reporting no duration can
        // never be recognised as finished, and one reporting no position can never be resumed.
        Console.WriteLine($"Position   {VodText.Time(_playback.Position)}");
        Console.WriteLine($"Duration   {VodText.Time(_playback.Duration)}");

        _progress.Observe(_playback.Position, _playback.Duration);

        if (await _progress.RecordAsync(cancellationToken).ConfigureAwait(false) is { } outcome)
        {
            Console.WriteLine($"Remembered  {outcome}");
        }
    }

    /// <summary>
    /// Moves a playing stream and reports where it ended up.
    /// </summary>
    /// <remarks>
    /// Held again afterwards, because a seek over HTTP is answered by a fresh range request and the engine
    /// reports the old position until that arrives. Reading immediately would say the seek did nothing on
    /// exactly the panels where it takes longest — which is the case worth knowing about.
    /// </remarks>
    private async Task SeekAndReportAsync(TimeSpan target, int seconds, CancellationToken cancellationToken)
    {
        if (!_playback.IsSeekable)
        {
            Console.Error.WriteLine(
                "Seek       refused; the panel serves this without range support, so it cannot be "
                + "positioned at all.");
            return;
        }

        Console.WriteLine($"Seeking to {target:hh\\:mm\\:ss}, then holding another {seconds}s.");
        _playback.SeekTo(target);

        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Sought to  {VodText.Time(_playback.Position)}");
    }
}
