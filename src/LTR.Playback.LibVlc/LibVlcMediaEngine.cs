using LibVLCSharp.Shared;
using LTR.Core.Playback;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    private Media? _currentMedia;
    private PlaybackState _state = PlaybackState.Idle;
    private TaskCompletionSource<bool>? _openCompletion;
    private TaskCompletionSource<bool>? _stopCompletion;
    private bool _isDisposed;

    public LibVlcMediaEngine(IOptions<LibVlcOptions> options, ILogger<LibVlcMediaEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        var resolvedOptions = options.Value;
        LibVlcRuntime.EnsureInitialized(resolvedOptions.NativeLibraryDirectory);

        _libVlc = new LibVLC(resolvedOptions.ToArguments());
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

    public async Task PlayAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _openCompletion = completion;

        var media = new Media(_libVlc, request.Url);

        // A per-input option, marked by the leading colon, so it applies to this stream only. Panels
        // filter on the agent, and a global setting would be wrong for a second source.
        media.AddOption($":http-user-agent={request.UserAgent}");

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
            SetState(PlaybackState.Stopped);
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

        return [.. descriptions.Select(description => ToMediaTrack(description, kind))];
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

        UnsubscribeFromPlayerEvents();

        _currentMedia?.Dispose();
        _currentMedia = null;
        _mediaPlayer.Dispose();
        _libVlc.Dispose();
    }

    private static MediaTrack ToMediaTrack(VlcTrackDescription description, MediaTrackKind kind)
    {
        return new MediaTrack(description.Id, kind, description.Name, Language: null);
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
        SetState(PlaybackState.Stopped);
        _stopCompletion?.TrySetResult(true);

        // A stop before the stream ever opened must release a pending PlayAsync as a failure, not
        // leave it waiting for an event that will never arrive.
        _openCompletion?.TrySetResult(false);
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        SetState(PlaybackState.Stopped);
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

    private void SetState(PlaybackState next, string? message = null)
    {
        var previous = _state;

        if (previous == next)
        {
            return;
        }

        _state = next;
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(previous, next, message));
    }
}
