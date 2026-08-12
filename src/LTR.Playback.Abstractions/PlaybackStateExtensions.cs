namespace LTR.Playback;

/// <summary>
/// Classifies <see cref="PlaybackState"/> values.
/// </summary>
public static class PlaybackStateExtensions
{
    /// <summary>
    /// Whether this state implies an open connection to the provider.
    /// </summary>
    /// <remarks>
    /// Drives the single-active-stream rule: while any state here is current, the account is
    /// consuming one of its permitted concurrent connections.
    /// </remarks>
    public static bool HoldsProviderConnection(this PlaybackState state)
    {
        return state is PlaybackState.Opening
            or PlaybackState.Buffering
            or PlaybackState.Playing
            or PlaybackState.Paused;
    }

    /// <summary>Whether this state is final for the current stream.</summary>
    public static bool IsTerminal(this PlaybackState state)
    {
        return state is PlaybackState.Idle or PlaybackState.Stopped or PlaybackState.Failed;
    }
}
