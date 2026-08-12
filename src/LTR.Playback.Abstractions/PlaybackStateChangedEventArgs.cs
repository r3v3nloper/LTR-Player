namespace LTR.Playback;

/// <summary>
/// Reports a playback state transition.
/// </summary>
public sealed class PlaybackStateChangedEventArgs : EventArgs
{
    public PlaybackStateChangedEventArgs(PlaybackState previous, PlaybackState current, string? message = null)
    {
        Previous = previous;
        Current = current;
        Message = message;
    }

    public PlaybackState Previous { get; }

    public PlaybackState Current { get; }

    /// <summary>Diagnostic detail, set when <see cref="Current"/> is <see cref="PlaybackState.Failed"/>.</summary>
    public string? Message { get; }
}
