using System.Collections.Concurrent;
using LTR.Core.Playback;

namespace LTR.Playback;

/// <summary>
/// A media engine that records what it was asked to do, and refuses to open two streams at once.
/// </summary>
/// <remarks>
/// The self-check in <see cref="PlayAsync"/> is the point of this fake: it stands in for the
/// provider, which reacts to a second concurrent connection by locking the account out. A test that
/// violates the ordering rule therefore fails here rather than passing quietly.
/// </remarks>
internal sealed class FakeMediaEngine : IMediaEngine
{
    private readonly ConcurrentQueue<string> _calls = new();
    private int _openStreams;

    public PlaybackState State { get; private set; } = PlaybackState.Idle;

    public int Volume { get; set; } = 100;

    public bool IsMuted { get; set; }

    /// <summary>Calls received, in order, as <c>stop</c> and <c>play:{name}</c> entries.</summary>
    public IReadOnlyList<string> Calls => [.. _calls];

    /// <summary>Whether a stream is currently held open.</summary>
    public bool HasOpenStream => Volatile.Read(ref _openStreams) > 0;

    public bool IsDisposed { get; private set; }

    /// <summary>Delay applied inside <see cref="StopAsync"/>, to model a slow release.</summary>
    public TimeSpan StopDelay { get; set; }

    /// <summary>When set, <see cref="StopAsync"/> throws this instead of releasing.</summary>
    public Exception? StopException { get; set; }

    /// <summary>When set, <see cref="PlayAsync"/> throws this instead of opening.</summary>
    public Exception? PlayException { get; set; }

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public async Task PlayAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        _calls.Enqueue($"play:{request.DisplayName}");

        if (PlayException is not null)
        {
            Transition(PlaybackState.Failed);
            throw PlayException;
        }

        if (Interlocked.Increment(ref _openStreams) > 1)
        {
            throw new InvalidOperationException(
                "A second stream was opened while one was already held. A real provider would treat "
                + "this as exceeding the connection limit.");
        }

        await Task.Yield();

        Transition(PlaybackState.Playing);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _calls.Enqueue("stop");

        if (StopDelay > TimeSpan.Zero)
        {
            await Task.Delay(StopDelay, cancellationToken).ConfigureAwait(false);
        }

        if (StopException is not null)
        {
            throw StopException;
        }

        // Clamped at zero so a stop with nothing open stays a harmless no-op.
        if (Volatile.Read(ref _openStreams) > 0)
        {
            Interlocked.Decrement(ref _openStreams);
        }

        Transition(PlaybackState.Stopped);
    }

    public void SetPaused(bool isPaused)
    {
        Transition(isPaused ? PlaybackState.Paused : PlaybackState.Playing);
    }

    public IReadOnlyList<MediaTrack> GetTracks(MediaTrackKind kind)
    {
        return [];
    }

    public void SelectTrack(MediaTrackKind kind, int trackId)
    {
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }

    private void Transition(PlaybackState next)
    {
        var previous = State;
        State = next;
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(previous, next));
    }
}
