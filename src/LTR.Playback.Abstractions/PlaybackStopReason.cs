namespace LTR.Playback;

/// <summary>
/// Why playback stopped.
/// </summary>
/// <remarks>
/// Exists because the two ways a stream ends are indistinguishable from the state alone, and the caller has
/// to treat them differently: a film that plays to its own end has been watched to the end and its position
/// is worth writing down, whereas the identical transition occurs in the middle of every channel change,
/// where the position was already recorded a moment earlier. Recording again there would overwrite a
/// deliberate position with whatever the engine happened to report while tearing down.
/// </remarks>
public enum PlaybackStopReason
{
    /// <summary>Not a stop. The state this accompanies is something other than <see cref="PlaybackState.Stopped"/>.</summary>
    None = 0,

    /// <summary>The application asked for the stream to be released.</summary>
    Requested = 1,

    /// <summary>The stream ran out on its own — the end of a film, or a provider dropping a channel.</summary>
    EndOfStream = 2,
}
