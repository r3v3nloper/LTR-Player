using LibVLCSharp.Shared;
using LTR.Core.Content;
using LTR.Core.Playback;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LibVlcLogLevel = LibVLCSharp.Shared.LogLevel;
using VlcTrackDescription = LibVLCSharp.Shared.Structures.TrackDescription;

namespace LTR.Playback.LibVlc;

/// <summary>
/// Plays streams through LibVLC.
/// </summary>
/// <remarks>
/// <para>
/// Not thread-safe by design; <see cref="PlaybackSession"/> serialises access. What this class does
/// guarantee is that <see cref="StopAsync"/> does not return until LibVLC has genuinely torn the
/// stream down, because the caller relies on that to keep within the provider's connection limit.
/// </para>
/// <para>
/// LibVLC raises its events on its own internal threads, and calling back into the player from one
/// of those threads deadlocks. Every completion source here therefore runs its continuations
/// asynchronously, which moves the follow-up work off the callback thread.
/// </para>
/// </remarks>
public sealed class LibVlcMediaEngine : IMediaEngine, IVlcVideoSink
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _mediaPlayer;
    private readonly ILogger<LibVlcMediaEngine> _logger;

    private readonly LibVlcOptions _options;

    private Media? _currentMedia;
    private PlaybackState _state = PlaybackState.Idle;
    private TaskCompletionSource<bool>? _openCompletion;
    private TaskCompletionSource<bool>? _stopCompletion;
    private VideoAspectRatio _aspectRatio = VideoAspectRatio.Source;
    private bool _isDisposed;

    public LibVlcMediaEngine(IOptions<LibVlcOptions> options, ILogger<LibVlcMediaEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        var resolvedOptions = options.Value;
        _options = resolvedOptions;
        LibVlcRuntime.EnsureInitialized(resolvedOptions.NativeLibraryDirectory);

        _libVlc = new LibVLC(resolvedOptions.ToArguments());
        _libVlc.Log += OnEngineLog;
        _mediaPlayer = new MediaPlayer(_libVlc);

        SubscribeToPlayerEvents();
    }

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public MediaPlayer MediaPlayer => _mediaPlayer;

    public PlaybackState State => _state;

    public int Volume
    {
        get => _mediaPlayer.Volume;
        set => _mediaPlayer.Volume = Math.Clamp(value, 0, 100);
    }

    public bool IsMuted
    {
        get => _mediaPlayer.Mute;
        set => _mediaPlayer.Mute = value;
    }

    /// <remarks>
    /// LibVLC answers with a negative number of milliseconds for a stream it cannot position, which is
    /// every live stream and any file it has not yet read enough of. That is reported as no position
    /// rather than as a time before the epoch.
    /// </remarks>
    public TimeSpan? Position => _isDisposed ? null : FromMilliseconds(_mediaPlayer.Time);

    public TimeSpan? Duration => _isDisposed ? null : FromMilliseconds(_mediaPlayer.Length);

    public bool IsSeekable => !_isDisposed && _mediaPlayer.IsSeekable;

    /// <remarks>
    /// Kept in a field as well as pushed at the player, because LibVLC forgets it whenever media is
    /// opened. A viewer who corrects a stretched channel and then zaps away and back would otherwise find
    /// the correction gone, which reads as the setting not having worked.
    /// </remarks>
    public VideoAspectRatio AspectRatio
    {
        get => _aspectRatio;

        set
        {
            _aspectRatio = value;
            ApplyAspectRatio();
        }
    }

    public async Task PlayAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _openCompletion = completion;

        var media = new Media(_libVlc, request.Url);

        // Per-input options, marked by the leading colon, so they apply to this stream only. Panels
        // filter on the agent, and a global setting would be wrong for a second source.
        media.AddOption($":http-user-agent={request.UserAgent}");
        media.AddOption(FormattableString.Invariant($":network-caching={CachingFor(request)}"));

        _currentMedia?.Dispose();
        _currentMedia = media;

        SetState(PlaybackState.Opening);

        if (!_mediaPlayer.Play(media))
        {
            SetState(PlaybackState.Failed, "LibVLC refused to start playback.");
            throw new PlaybackFailedException($"LibVLC refused to open {request.DisplayName}.", request);
        }

        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
            completion);

        bool opened;

        try
        {
            // Resolves on the first Playing or EncounteredError event.
            opened = await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            // Cleared so a later event cannot resolve this attempt's completion source.
            _openCompletion = null;
        }

        if (!opened)
        {
            throw new PlaybackFailedException(
                $"Could not play {request.DisplayName}. The channel may be offline, or the provider "
                + "may have refused the connection.",
                request);
        }

        // Both only now that the stream is open: LibVLC discards a ratio set against no media, and a seek
        // issued before the demuxer has its index is answered by prerolling the whole file.
        ApplyAspectRatio();
        ApplyStartPosition(request);
    }

    /// <summary>
    /// How much of a stream to buffer before playing it.
    /// </summary>
    /// <remarks>
    /// Live television gets its own figure because this is the one lever that shortens a channel change.
    /// The other half of a zap — releasing the previous stream and waiting for the provider to notice — is
    /// mandatory and cannot be tuned; the buffer is what is left, and every millisecond of it is spent
    /// before the first frame appears. It is a separate value rather than a lower single one because a film
    /// gains nothing from a short buffer and loses its resilience to a stalled read.
    /// </remarks>
    private int CachingFor(MediaRequest request)
    {
        return request.Format == StreamFormat.ProgressiveFile
            ? _options.NetworkCachingMilliseconds
            : _options.LiveNetworkCachingMilliseconds;
    }

    /// <summary>
    /// Moves to a resume position once the stream is open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After opening rather than through LibVLC's <c>start-time</c> option, and that order was arrived at
    /// by measurement rather than by preference. Handed <c>start-time</c>, the Matroska demuxer issues its
    /// seek before it has read the file's cues and answers it by prerolling from byte 1036 — reading the
    /// whole file forward to reach the requested moment. Against a remote film that never arrives: the
    /// stream reports itself as playing while its position stays unknown for minutes on end.
    /// </para>
    /// <para>
    /// Issued after the first Playing event, the same seek uses the loaded index and lands immediately. The
    /// cost is that a fraction of a second of the opening plays first, which is a great deal better than a
    /// resume that never completes.
    /// </para>
    /// </remarks>
    private void ApplyStartPosition(MediaRequest request)
    {
        if (request.StartAt is not { TotalMilliseconds: > 0 } startAt)
        {
            return;
        }

        if (!_mediaPlayer.IsSeekable)
        {
            // Live streams, and the occasional film served without range support. Nothing to be done, and
            // playing from the start beats refusing to play.
            LibVlcLog.ResumeNotSeekable(_logger, request.DisplayName);
            return;
        }

        _mediaPlayer.Time = (long)startAt.TotalMilliseconds;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            return;
        }

        // Nothing is held, so there is no provider connection to release.
        if (_mediaPlayer.State is VLCState.NothingSpecial or VLCState.Stopped or VLCState.Ended)
        {
            ReleaseMedia();
            SetState(PlaybackState.Stopped, reason: PlaybackStopReason.Requested);
            return;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _stopCompletion = completion;

        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
            completion);

        // Stop() blocks until LibVLC's internal threads wind down, and deadlocks when called from a
        // LibVLC callback thread. Moving it to the thread pool keeps both problems away.
        await Task.Run(_mediaPlayer.Stop, CancellationToken.None).ConfigureAwait(false);

        try
        {
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _stopCompletion = null;
        }

        ReleaseMedia();
    }

    public void SetPaused(bool isPaused)
    {
        if (_isDisposed || !_mediaPlayer.CanPause)
        {
            return;
        }

        _mediaPlayer.SetPause(isPaused);
    }

    /// <remarks>
    /// Refused rather than clamped for an unseekable stream. Live television has no position to move to,
    /// and pretending a seek happened would leave a seek bar showing a place playback is not at.
    /// </remarks>
    public void SeekTo(TimeSpan position)
    {
        if (_isDisposed || !_mediaPlayer.IsSeekable)
        {
            return;
        }

        var target = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        _mediaPlayer.Time = (long)target.TotalMilliseconds;
    }

    public IReadOnlyList<MediaTrack> GetTracks(MediaTrackKind kind)
    {
        if (_isDisposed)
        {
            return [];
        }

        var descriptions = kind switch
        {
            MediaTrackKind.Audio => _mediaPlayer.AudioTrackDescription,
            MediaTrackKind.Subtitle => _mediaPlayer.SpuDescription,
            MediaTrackKind.Video => _mediaPlayer.VideoTrackDescription,
            _ => [],
        };

        // LibVLC prepends a synthetic "Disable" entry with a negative id, meant as a menu command for
        // switching the track off rather than as a track. Passing it on would offer "Disable" as a
        // selectable audio language.
        return
        [
            .. descriptions
                .Where(description => description.Id >= 0)
                .Select(description => ToMediaTrack(description, kind)),
        ];
    }

    public int GetSelectedTrack(MediaTrackKind kind)
    {
        if (_isDisposed)
        {
            return MediaTrack.DisabledId;
        }

        return kind switch
        {
            MediaTrackKind.Audio => _mediaPlayer.AudioTrack,
            MediaTrackKind.Subtitle => _mediaPlayer.Spu,
            MediaTrackKind.Video => _mediaPlayer.VideoTrack,
            _ => MediaTrack.DisabledId,
        };
    }

    public void SelectTrack(MediaTrackKind kind, int trackId)
    {
        if (_isDisposed)
        {
            return;
        }

        switch (kind)
        {
            case MediaTrackKind.Audio:
                _mediaPlayer.SetAudioTrack(trackId);
                break;

            case MediaTrackKind.Subtitle:
                _mediaPlayer.SetSpu(trackId);
                break;

            case MediaTrackKind.Video:
                _mediaPlayer.SetVideoTrack(trackId);
                break;

            default:
                break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        // Release before tearing down, so the provider stops counting the connection. Bounded, because
        // a wedged engine must not prevent the process from exiting.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LibVlcLog.ShutdownStopTimedOut(_logger);
        }

        _isDisposed = true;

        _libVlc.Log -= OnEngineLog;
        UnsubscribeFromPlayerEvents();

        _currentMedia?.Dispose();
        _currentMedia = null;
        _mediaPlayer.Dispose();
        _libVlc.Dispose();
    }

    /// <summary>
    /// Turns LibVLC's millisecond figure into a duration, treating anything not positive as unknown.
    /// </summary>
    private static TimeSpan? FromMilliseconds(long milliseconds)
    {
        return milliseconds > 0 ? TimeSpan.FromMilliseconds(milliseconds) : null;
    }

    /// <remarks>
    /// No language is reported. LibVLC's track description carries a name and nothing else, and for the
    /// streams this player sees that name is what the muxer wrote — usually the language already
    /// ("Deutsch", "English - [English]"), occasionally nothing at all, which is what
    /// <see cref="MediaTrack.DisplayLabel"/> covers.
    /// </remarks>
    private static MediaTrack ToMediaTrack(VlcTrackDescription description, MediaTrackKind kind)
    {
        return new MediaTrack(description.Id, kind, description.Name, Language: null);
    }

    /// <summary>
    /// Pushes the chosen ratio at the player, in the string form LibVLC takes.
    /// </summary>
    /// <remarks>
    /// An empty string, not <see langword="null"/>, restores the stream's own ratio: LibVLC treats null as
    /// "leave whatever is set", so assigning it would make the setting one-way.
    /// </remarks>
    private void ApplyAspectRatio()
    {
        if (_isDisposed)
        {
            return;
        }

        _mediaPlayer.AspectRatio = _aspectRatio switch
        {
            VideoAspectRatio.Widescreen => "16:9",
            VideoAspectRatio.Standard => "4:3",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Routes LibVLC's own diagnostics into the application log.
    /// </summary>
    /// <remarks>
    /// LibVLC's error level is downgraded to a warning deliberately. Nearly all of it describes the
    /// stream rather than the player — corrupt H.264 references, missing audio headers, a streaming
    /// node refusing a connection — and treating a provider's broken channel as an application error
    /// would make the log useless for finding actual faults.
    /// </remarks>
    private void OnEngineLog(object? sender, LogEventArgs e)
    {
        var module = e.Module ?? "core";
        var detail = e.Message ?? string.Empty;

        if (e.Level == LibVlcLogLevel.Error)
        {
            LibVlcLog.EngineWarning(_logger, module, detail);
            return;
        }

        LibVlcLog.EngineDetail(_logger, module, detail);
    }

    private void SubscribeToPlayerEvents()
    {
        _mediaPlayer.Opening += OnOpening;
        _mediaPlayer.Buffering += OnBuffering;
        _mediaPlayer.Playing += OnPlaying;
        _mediaPlayer.Paused += OnPaused;
        _mediaPlayer.Stopped += OnStopped;
        _mediaPlayer.EndReached += OnEndReached;
        _mediaPlayer.EncounteredError += OnEncounteredError;
    }

    private void UnsubscribeFromPlayerEvents()
    {
        _mediaPlayer.Opening -= OnOpening;
        _mediaPlayer.Buffering -= OnBuffering;
        _mediaPlayer.Playing -= OnPlaying;
        _mediaPlayer.Paused -= OnPaused;
        _mediaPlayer.Stopped -= OnStopped;
        _mediaPlayer.EndReached -= OnEndReached;
        _mediaPlayer.EncounteredError -= OnEncounteredError;
    }

    private void OnOpening(object? sender, EventArgs e)
    {
        SetState(PlaybackState.Opening);
    }

    private void OnBuffering(object? sender, MediaPlayerBufferingEventArgs e)
    {
        // Buffering fires continuously up to 100%; only the start is a state change worth reporting.
        if (_state != PlaybackState.Playing)
        {
            SetState(PlaybackState.Buffering);
        }
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        SetState(PlaybackState.Playing);
        _openCompletion?.TrySetResult(true);
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        SetState(PlaybackState.Paused);
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        SetState(PlaybackState.Stopped, reason: PlaybackStopReason.Requested);
        _stopCompletion?.TrySetResult(true);

        // A stop before the stream ever opened must release a pending PlayAsync as a failure, not
        // leave it waiting for an event that will never arrive.
        _openCompletion?.TrySetResult(false);
    }

    /// <remarks>
    /// The reason is the whole point of handling this separately from <see cref="OnStopped"/>. LibVLC
    /// raises End Reached and then Stopped, and the second one is swallowed by the deduplication in
    /// <see cref="SetState"/> — so reporting the reason here is what lets a caller tell a film that ran to
    /// its own end from one the viewer stopped. Getting that the wrong way round would have every channel
    /// change look like a completed programme.
    /// </remarks>
    private void OnEndReached(object? sender, EventArgs e)
    {
        SetState(PlaybackState.Stopped, reason: PlaybackStopReason.EndOfStream);
        _stopCompletion?.TrySetResult(true);
        _openCompletion?.TrySetResult(false);
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        SetState(PlaybackState.Failed, "LibVLC reported a playback error.");
        _openCompletion?.TrySetResult(false);

        // An error also ends the stream, so a stop waiting on teardown must not hang.
        _stopCompletion?.TrySetResult(true);
    }

    private void ReleaseMedia()
    {
        _currentMedia?.Dispose();
        _currentMedia = null;
    }

    private void SetState(
        PlaybackState next,
        string? message = null,
        PlaybackStopReason reason = PlaybackStopReason.None)
    {
        var previous = _state;

        if (previous == next)
        {
            return;
        }

        _state = next;
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(previous, next, message, reason));
    }
}
