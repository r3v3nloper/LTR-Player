using System.Diagnostics;
using LTR.Core.Playback;
using Microsoft.Extensions.Logging;

namespace LTR.Playback;

/// <summary>
/// Serialises all playback through one engine so that at most one provider connection is ever held.
/// </summary>
/// <remarks>
/// <para>
/// The ordering guarantee is the whole point of this class. IPTV subscriptions permit very few
/// concurrent connections — often one — and a provider counts a connection as open until the client
/// actually closes it. Starting a new stream before the previous one is released therefore does not
/// merely waste bandwidth, it trips the provider's limit and locks the account out for minutes.
/// </para>
/// <para>
/// Two consequences shape the implementation. The stop step is never abandoned, not even when the
/// caller cancels or a newer request arrives, because an abandoned stop is exactly the leak this
/// class exists to prevent. And rapid channel changes are resolved by generation, so intermediate
/// requests are dropped instead of each being opened in turn.
/// </para>
/// </remarks>
public sealed class PlaybackSession : IPlaybackSession, IPlaybackTransport
{
    /// <summary>
    /// How long the engine is given to release a stream before the attempt is abandoned and logged.
    /// Generous, because giving up early is what leaks a connection; bounded, because a hung engine
    /// must not freeze the application forever.
    /// </summary>
    public static readonly TimeSpan DefaultStopTimeout = TimeSpan.FromSeconds(10);

    private readonly IMediaEngine _engine;
    private readonly ILogger<PlaybackSession> _logger;
    private readonly TimeSpan _stopTimeout;

    /// <summary>Serialises switches so a stop and the start that follows it cannot interleave.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Incremented per request. A switch that finds a newer generation after stopping steps aside
    /// instead of opening a stream the user has already zapped past.
    /// </summary>
    private long _generation;

    private MediaRequest? _current;
    private bool _isDisposed;

    /// <param name="stopTimeout">
    /// Overrides <see cref="DefaultStopTimeout"/>. Present so the hung-engine path can be tested
    /// without the test taking as long as the production timeout.
    /// </param>
    public PlaybackSession(
        IMediaEngine engine,
        ILogger<PlaybackSession> logger,
        TimeSpan? stopTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(logger);

        _engine = engine;
        _logger = logger;
        _stopTimeout = stopTimeout ?? DefaultStopTimeout;
        _engine.StateChanged += OnEngineStateChanged;
    }

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public PlaybackState State => _engine.State;

    public MediaRequest? Current => _current;

    public TimeSpan? Position => _isDisposed ? null : _engine.Position;

    public TimeSpan? Duration => _isDisposed ? null : _engine.Duration;

    public bool IsSeekable => !_isDisposed && _engine.IsSeekable;

    /// <remarks>
    /// Every transport member is a delegation, and deliberately so. They exist here because the on-screen
    /// controls need them and must not hold the engine; this class adds nothing to them beyond refusing to
    /// touch a disposed engine, which is reachable — the window's sampling timer can tick once more while
    /// the container is being torn down.
    /// </remarks>
    public int Volume
    {
        get => _isDisposed ? 0 : _engine.Volume;
        set
        {
            if (!_isDisposed)
            {
                _engine.Volume = value;
            }
        }
    }

    public bool IsMuted
    {
        get => !_isDisposed && _engine.IsMuted;
        set
        {
            if (!_isDisposed)
            {
                _engine.IsMuted = value;
            }
        }
    }

    public VideoAspectRatio AspectRatio
    {
        get => _isDisposed ? VideoAspectRatio.Source : _engine.AspectRatio;
        set
        {
            if (!_isDisposed)
            {
                _engine.AspectRatio = value;
            }
        }
    }

    public async Task<PlaybackState> SwitchToAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var generation = Interlocked.Increment(ref _generation);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Always release first. Deliberately not passed the caller's token: cancelling a stop is
            // what leaves the provider counting a connection nobody is using.
            await StopCoreAsync().ConfigureAwait(false);

            if (Interlocked.Read(ref _generation) != generation)
            {
                PlaybackLog.SwitchSuperseded(_logger, request.DisplayName);
                return _engine.State;
            }

            cancellationToken.ThrowIfCancellationRequested();

            PlaybackLog.Switching(_logger, request.DisplayName);

            // Timed because zapping latency is otherwise unmeasurable: the wait a viewer notices is spread
            // across releasing the old stream, the panel answering and the engine filling its buffer, and
            // no setting can be judged without knowing which of those the time went into. The stop is
            // outside this figure on purpose — it is mandatory and cannot be tuned.
            var openStarted = Stopwatch.GetTimestamp();

            try
            {
                await _engine.PlayAsync(request, cancellationToken).ConfigureAwait(false);
                _current = request;

                PlaybackLog.Opened(
                    _logger,
                    request.DisplayName,
                    Stopwatch.GetElapsedTime(openStarted).TotalMilliseconds);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                PlaybackLog.PlayFailed(_logger, exception, request.DisplayName);

                // A failed start may still have opened a connection, so release it before reporting.
                await StopCoreAsync().ConfigureAwait(false);
                throw;
            }

            return _engine.State;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        // Bumping the generation makes any switch waiting on the gate stand down rather than start
        // a stream after the user asked for silence.
        Interlocked.Increment(ref _generation);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void SetPaused(bool isPaused)
    {
        if (!_isDisposed)
        {
            _engine.SetPaused(isPaused);
        }
    }

    public void SeekTo(TimeSpan position)
    {
        if (!_isDisposed)
        {
            _engine.SeekTo(position);
        }
    }

    public IReadOnlyList<MediaTrack> GetTracks(MediaTrackKind kind)
    {
        return _isDisposed ? [] : _engine.GetTracks(kind);
    }

    public int GetSelectedTrack(MediaTrackKind kind)
    {
        return _isDisposed ? MediaTrack.DisabledId : _engine.GetSelectedTrack(kind);
    }

    public void SelectTrack(MediaTrackKind kind, int trackId)
    {
        if (!_isDisposed)
        {
            _engine.SelectTrack(kind, trackId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _engine.StateChanged -= OnEngineStateChanged;

        // Runs even on an unclean shutdown, because a process that exits holding a stream is the
        // most common way users end up locked out of their own subscription.
        await StopCoreAsync().ConfigureAwait(false);
        await _engine.DisposeAsync().ConfigureAwait(false);

        _gate.Dispose();
    }

    /// <summary>
    /// Releases the current stream, tolerating an engine that fails or hangs.
    /// </summary>
    /// <remarks>
    /// Never throws. Callers use this on paths where the alternative to a best-effort release is no
    /// release at all, so an exception here would make matters worse.
    /// </remarks>
    private async Task StopCoreAsync()
    {
        using var timeout = new CancellationTokenSource(_stopTimeout);

        try
        {
            await _engine.StopAsync(timeout.Token).ConfigureAwait(false);
            _current = null;
        }
        catch (OperationCanceledException)
        {
            PlaybackLog.StopTimedOut(_logger, _stopTimeout.TotalSeconds);
        }
        catch (Exception exception)
        {
            PlaybackLog.StopFailed(_logger, exception);
        }
    }

    private void OnEngineStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        StateChanged?.Invoke(this, e);
    }
}
