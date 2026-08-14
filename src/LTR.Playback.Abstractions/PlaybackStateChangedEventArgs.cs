namespace LTR.Playback;

/// <summary>
/// Reports a playback state transition.
/// </summary>
public sealed class PlaybackStateChangedEventArgs : EventArgs
{
    public PlaybackStateChangedEventArgs(
        PlaybackState previous,
        PlaybackState current,
        string? message = null,
        PlaybackStopReason reason = PlaybackStopReason.None)
    {
        Previous = previous;
        Current = current;
        Message = message;
        Reason = reason;
    }

    public PlaybackState Previous { get; }

    public PlaybackState Current { get; }

    /// <summary>Diagnostic detail, set when <see cref="Current"/> is <see cref="PlaybackState.Failed"/>.</summary>
    public string? Message { get; }

    /// <summary>
    /// Why the stream stopped, when <see cref="Current"/> is <see cref="PlaybackState.Stopped"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="PlaybackStopReason.None"/> for every other transition, and for a stop an engine cannot
    /// account for. A caller acting on the difference therefore has to ask for the reason it wants rather
    /// than treat anything that is not one as the other.
    /// </remarks>
    public PlaybackStopReason Reason { get; }
}
