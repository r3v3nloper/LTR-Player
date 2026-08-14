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

    /// <summary>Where the fake reports playback to have reached, set by a test.</summary>
    public TimeSpan? Position { get; set; }

    public TimeSpan? Duration { get; set; }

    public bool IsSeekable { get; set; }

    public VideoAspectRatio AspectRatio { get; set; } = VideoAspectRatio.Source;

    /// <summary>Where <see cref="SeekTo"/> was last asked to move, or null when it was refused.</summary>
    public TimeSpan? SeekedTo { get; private set; }

    /// <summary>Tracks the fake reports, keyed by kind and set by a test.</summary>
    public Dictionary<MediaTrackKind, IReadOnlyList<MediaTrack>> Tracks { get; } = [];

    /// <summary>Track selections received, in order.</summary>
    public List<(MediaTrackKind Kind, int TrackId)> SelectedTracks { get; } = [];

    /// <summary>Calls received, in order, as <c>stop</c> and <c>play:{name}</c> entries.</summary>
    public IReadOnlyList<string> Calls => [.. _calls];

    /// <summary>
    /// The position each opened stream asked to start at, so a test can prove a resume was honoured while
    /// opening rather than by a later seek.
    /// </summary>
    public List<TimeSpan?> RequestedStartPositions { get; } = [];

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
        RequestedStartPositions.Add(request.StartAt);

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

    /// <remarks>
    /// Refuses an unseekable stream exactly as the real engine does, so a test asserting that live
    /// television cannot be positioned is testing the rule rather than the fake.
    /// </remarks>
    public void SeekTo(TimeSpan position)
    {
        if (IsSeekable)
        {
            SeekedTo = position;
        }
    }

    public IReadOnlyList<MediaTrack> GetTracks(MediaTrackKind kind)
    {
        return Tracks.GetValueOrDefault(kind, []);
    }

    /// <summary>Reports back the last selection made, as an engine does.</summary>
    public int GetSelectedTrack(MediaTrackKind kind)
    {
        var ofKind = SelectedTracks.Where(selection => selection.Kind == kind).ToList();

        return ofKind.Count > 0 ? ofKind[^1].TrackId : MediaTrack.DisabledId;
    }

    public void SelectTrack(MediaTrackKind kind, int trackId)
    {
        SelectedTracks.Add((kind, trackId));
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
