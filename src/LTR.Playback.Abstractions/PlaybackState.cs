namespace LTR.Playback;

/// <summary>
/// Lifecycle of a playback attempt.
/// </summary>
public enum PlaybackState
{
    /// <summary>Nothing has been opened.</summary>
    Idle = 0,

    /// <summary>A stream is being opened; the provider connection is already established.</summary>
    Opening = 1,

    Buffering = 2,

    Playing = 3,

    Paused = 4,

    /// <summary>Playback ended and the provider connection has been released.</summary>
    Stopped = 5,

    /// <summary>Playback could not start or was aborted by an error.</summary>
    Failed = 6,
}
