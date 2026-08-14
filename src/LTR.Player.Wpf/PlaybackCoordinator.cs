using CommunityToolkit.Mvvm.ComponentModel;
using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Playback;
using LTR.Providers;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Opens what the viewer picked, and remembers where they got to.
/// </summary>
/// <remarks>
/// <para>
/// Lifted out of the shell view model, which had grown back past the size that earned it a backlog entry
/// once already: composition, section wiring, the guide, playback and progress. Everything about a stream —
/// building its address, opening it, wording the failure, following the position and writing it down — is
/// here, and nothing else opens one.
/// </para>
/// <para>
/// That single ownership is the point rather than a tidiness argument. A subscription permits very few
/// concurrent connections, and two places able to start a stream is how one gets left open. The shell keeps
/// the commands, because they belong to the window; each is two lines that delegate here.
/// </para>
/// <para>
/// What has to happen *after* progress is recorded stays with the caller, as a continuation it supplies:
/// three separate lists display a stored position, and knowing about all three is the shell's business.
/// </para>
/// </remarks>
public sealed partial class PlaybackCoordinator : ObservableObject
{
    private readonly IProviderRegistry _providers;
    private readonly IPlaybackSession _session;
    private readonly WatchProgressRecorder _progress;
    private readonly StatusLine _status;
    private readonly ILogger<PlaybackCoordinator> _logger;

    [ObservableProperty]
    private string _nowPlaying = string.Empty;

    /// <summary>
    /// Set when the engine reports a stream having run out on its own, cleared when that is acted on.
    /// </summary>
    /// <remarks>
    /// A flag rather than the work itself, because the report arrives on one of the engine's own threads and
    /// what has to follow it — a database write, then three lists rereading themselves — belongs on the
    /// thread the window lives on. <see cref="SampleAsync"/> picks it up on the next tick, which is at most a
    /// few seconds later and long after anyone has stopped watching.
    /// </remarks>
    private int _hasReachedEndOfStream;

    public PlaybackCoordinator(
        IProviderRegistry providers,
        IPlaybackSession session,
        WatchProgressRecorder progress,
        StatusLine status,
        ILogger<PlaybackCoordinator> logger)
    {
        _providers = providers;
        _session = session;
        _progress = progress;
        _status = status;
        _logger = logger;

        _session.StateChanged += OnPlaybackStateChanged;
    }

    /// <summary>
    /// Run after a position has been written, so that whatever displays one can be brought up to date.
    /// </summary>
    /// <remarks>
    /// Assigned by the shell, which is the only thing that knows the three lists a position appears in.
    /// Inert until then, so nothing here has to check it.
    /// </remarks>
    public Func<CancellationToken, Task> ProgressRecorded { get; set; } = _ => Task.CompletedTask;

    /// <summary>Whether something with a resume position is being followed.</summary>
    public bool IsFollowingProgress => _progress.IsTracking;

    /// <summary>
    /// Samples where playback has reached, so a position survives the stream being closed.
    /// </summary>
    /// <remarks>
    /// Driven by a timer, because by the time playback has stopped the engine no longer has a position to
    /// report — a recorder that only looked when asked to save would always save nothing.
    /// </remarks>
    public void ObservePosition()
    {
        if (_progress.IsTracking)
        {
            _progress.Observe(_session.Position, _session.Duration);
        }
    }

    /// <summary>
    /// Takes a sample, and closes off a stream that has ended by itself.
    /// </summary>
    /// <remarks>
    /// The end of a film is not a stop anyone asked for, so nothing else brings it to the point where a
    /// position gets written: a film that plays out and sits there would stay on the continue-watching list
    /// until the next channel change or the window closing. Handled from the sampling tick rather than from
    /// the engine's own report, because that report arrives on an engine thread.
    /// </remarks>
    public async Task SampleAsync(CancellationToken cancellationToken)
    {
        ObservePosition();

        if (Interlocked.Exchange(ref _hasReachedEndOfStream, 0) == 0)
        {
            return;
        }

        var ended = NowPlaying;

        // A full stop, not just a recording. The engine has finished with the stream but the session still
        // holds it as current, and a provider that is still counting the connection is the one problem this
        // application exists to avoid.
        await StopAsync(cancellationToken).ConfigureAwait(true);

        _status.Text = string.IsNullOrEmpty(ended) ? "Playback ended." : $"{ended} ended.";
    }

    /// <summary>
    /// Plays a live channel. Nothing about a channel is remembered: it has no position and nothing to resume.
    /// </summary>
    public async Task PlayChannelAsync(
        PlaylistSource source,
        Channel channel,
        string displayName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(channel);

        // Recorded before the switch, while the samples still describe what was playing. A no-op unless a
        // film was open.
        await RecordProgressAsync(cancellationToken).ConfigureAwait(true);

        var request = _providers.GetStreamUrlResolver(source).ResolveLive(source, channel);
        await StartAsync(request, displayName, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Plays a film, from <paramref name="startAt"/> when it is resuming.
    /// </summary>
    public async Task PlayMovieAsync(
        PlaylistSource source,
        VodItem movie,
        TimeSpan? startAt,
        string displayName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(movie);

        var request = _providers.GetStreamUrlResolver(source).ResolveMovie(source, movie, startAt);

        await PlayResumableAsync(
                ContentKind.Movie,
                movie.Id,
                request,
                displayName,
                startAt,
                cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Plays one episode. Takes the episode alone, because its address is built from its own identifier.
    /// </summary>
    public async Task PlayEpisodeAsync(
        PlaylistSource source,
        Episode episode,
        TimeSpan? startAt,
        string displayName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(episode);

        var request = _providers.GetStreamUrlResolver(source).ResolveEpisode(source, episode, startAt);

        await PlayResumableAsync(
                ContentKind.Series,
                episode.Id,
                request,
                displayName,
                startAt,
                cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Releases the stream, and writes down where it got to.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _session.StopAsync(cancellationToken).ConfigureAwait(true);
        NowPlaying = string.Empty;

        await RecordProgressAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Hands the connection back on the way out of the window, taking a final sample first.
    /// </summary>
    /// <remarks>
    /// The last timer tick may be seconds old, and those seconds are the viewer's place in the film they
    /// were watching when they closed the window. The release itself is deliberately not cancellable: a
    /// subscription permitting a single connection is unusable for minutes if the player exits holding one.
    /// </remarks>
    public async Task ShutdownAsync()
    {
        ObservePosition();
        await StopAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>
    /// Stops following whatever is being watched, without writing anything down.
    /// </summary>
    /// <remarks>
    /// For an item the viewer has just taken off the continue-watching list. Left followed, stopping playback
    /// afterwards would write its position straight back and the entry would return.
    /// </remarks>
    public void StopFollowing()
    {
        _progress.Forget();
    }

    /// <summary>
    /// Writes down where the viewer got to, and lets the caller refresh what shows it.
    /// </summary>
    public async Task RecordProgressAsync(CancellationToken cancellationToken)
    {
        if (!_progress.IsTracking)
        {
            return;
        }

        ObservePosition();
        await _progress.RecordAsync(cancellationToken).ConfigureAwait(true);

        try
        {
            await ProgressRecorded(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The window is closing; the position itself is already written.
        }
    }

    /// <summary>
    /// Starts a film or episode and begins following where it gets to.
    /// </summary>
    private async Task PlayResumableAsync(
        ContentKind kind,
        int itemId,
        MediaRequest request,
        string displayName,
        TimeSpan? startAt,
        CancellationToken cancellationToken)
    {
        await RecordProgressAsync(cancellationToken).ConfigureAwait(true);

        // Followed from the position playback was asked to start at, not from zero. A viewer who resumes at
        // forty minutes and closes the window before the first sample arrives would otherwise have their
        // place reset to the beginning — and a deep seek can take longer than the first sample.
        _progress.Track(kind, itemId, startAt ?? TimeSpan.Zero);

        if (!await StartAsync(request, displayName, cancellationToken).ConfigureAwait(true))
        {
            // Nothing was watched, so there is nothing to remember — and leaving the recorder following a
            // film that never opened would attribute the next stop to it.
            _progress.Forget();
        }
    }

    /// <summary>
    /// Opens a stream and reports whether it started.
    /// </summary>
    private async Task<bool> StartAsync(
        MediaRequest request,
        string displayName,
        CancellationToken cancellationToken)
    {
        NowPlaying = displayName;

        try
        {
            await _session.SwitchToAsync(request, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (PlaybackFailedException exception)
        {
            // Expected in daily use: providers take channels offline without notice, and a subscription
            // permitting one connection refuses the next stream until it notices the last one closed.
            PlayerLog.ChannelUnplayable(_logger, exception, displayName);
            _status.Text = $"{displayName} could not be played. It may be offline, or the subscription's "
                + "one connection may still be in use.";
            NowPlaying = string.Empty;

            return false;
        }
        catch (OperationCanceledException)
        {
            // Zapping onwards cancels the open that was still in flight. That is the intended behaviour of a
            // channel change, not a failure — and left unhandled it surfaces as an error dialog for an
            // ordinary key press.
            return false;
        }
    }

    /// <remarks>
    /// Raised on one of the engine's own threads. Assigning an observable property from there is safe —
    /// WPF marshals a property change for a plain binding — but nothing here may touch a collection or await
    /// anything, which is why the end of a stream is only noted and acted on elsewhere.
    /// </remarks>
    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        if (e.Current == PlaybackState.Playing)
        {
            _status.Text = $"Playing {NowPlaying}";
            return;
        }

        // Only when the stream ran out. The identical transition happens in the middle of every channel
        // change, where progress was recorded a moment earlier and recording it again would overwrite a
        // deliberate position with whatever the engine reported while tearing down.
        if (e.Reason == PlaybackStopReason.EndOfStream)
        {
            Interlocked.Exchange(ref _hasReachedEndOfStream, 1);
        }
    }
}
