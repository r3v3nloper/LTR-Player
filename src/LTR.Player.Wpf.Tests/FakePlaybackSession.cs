using LTR.Core.Playback;
using LTR.Playback;

namespace LTR.Player.Wpf;

/// <summary>
/// Records playback requests without opening anything.
/// </summary>
internal sealed class FakePlaybackSession : IPlaybackSession
{
    public List<MediaRequest> Started { get; } = [];

    public int StopCount { get; private set; }

    /// <summary>When set, <see cref="SwitchToAsync"/> throws it instead of starting.</summary>
    public Exception? SwitchException { get; set; }

    public PlaybackState State { get; private set; } = PlaybackState.Idle;

    public MediaRequest? Current { get; private set; }

    /// <summary>Where the fake reports playback to have reached, set by a test.</summary>
    public TimeSpan? Position { get; set; }

    public TimeSpan? Duration { get; set; }

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public Task<PlaybackState> SwitchToAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        if (SwitchException is not null)
        {
            return Task.FromException<PlaybackState>(SwitchException);
        }

        Started.Add(request);
        Current = request;
        Transition(PlaybackState.Playing);

        return Task.FromResult(State);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopCount++;
        Current = null;

        // Cleared as a real engine does, which is what makes the last polled position the only figure a
        // caller can record progress from once playback has stopped.
        Position = null;
        Duration = null;

        Transition(PlaybackState.Stopped);

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private void Transition(PlaybackState next)
    {
        var previous = State;
        State = next;
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(previous, next));
    }
}
