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

    /// <summary>Whether the fake admits a seek. False by default, as live television is.</summary>
    public bool IsSeekable { get; set; }

    public int Volume { get; set; } = 100;

    public bool IsMuted { get; set; }

    public VideoAspectRatio AspectRatio { get; set; } = VideoAspectRatio.Source;

    public bool IsPaused { get; private set; }

    /// <summary>Where <see cref="SeekTo"/> was last asked to move, or null when it was refused.</summary>
    public TimeSpan? SeekedTo { get; private set; }

    /// <summary>Tracks the fake reports, keyed by kind and set by a test.</summary>
    public Dictionary<MediaTrackKind, IReadOnlyList<MediaTrack>> Tracks { get; } = [];

    /// <summary>Track selections received, in order.</summary>
    public List<(MediaTrackKind Kind, int TrackId)> SelectedTracks { get; } = [];

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public Task<PlaybackState> SwitchToAsync(MediaRequest request, CancellationToken cancellationToken)
    {
        if (SwitchException is not null)
        {
            return Task.FromException<PlaybackState>(SwitchException);
        }

        Started.Add(request);
        Current = request;
        IsPaused = false;
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

        Transition(PlaybackState.Stopped, PlaybackStopReason.Requested);

        return Task.CompletedTask;
    }

    public void SetPaused(bool isPaused)
    {
        IsPaused = isPaused;
        Transition(isPaused ? PlaybackState.Paused : PlaybackState.Playing);
    }

    /// <remarks>
    /// Refuses an unseekable stream as the real engine does, so a test asserting that live television
    /// cannot be positioned is testing the rule and not the fake.
    /// </remarks>
    public void SeekTo(TimeSpan position)
    {
        if (!IsSeekable)
        {
            return;
        }

        SeekedTo = position;
        Position = position;
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

    /// <summary>
    /// Reports that the stream ran out on its own, as an engine does at the end of a film.
    /// </summary>
    /// <remarks>
    /// Exposed because a test cannot otherwise reach the transition that matters: the state is the same one
    /// every channel change passes through, and only the reason distinguishes them.
    /// </remarks>
    public void ReachEndOfStream()
    {
        Position = null;
        Duration = null;
        Transition(PlaybackState.Stopped, PlaybackStopReason.EndOfStream);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private void Transition(PlaybackState next, PlaybackStopReason reason = PlaybackStopReason.None)
    {
        var previous = State;
        State = next;
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(previous, next, message: null, reason));
    }
}
