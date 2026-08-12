using LTR.Core.Playback;

namespace LTR.Playback;

/// <summary>
/// A stream could not be opened or was aborted by the engine.
/// </summary>
/// <remarks>
/// A distinct type rather than <see cref="InvalidOperationException"/>, because an unplayable channel
/// is an expected outcome in IPTV, not a programming error. Callers need to tell the two apart to
/// report the first as a message and the second as a crash.
/// </remarks>
public sealed class PlaybackFailedException : Exception
{
    public PlaybackFailedException(string message, MediaRequest? request = null)
        : base(message)
    {
        Request = request;
    }

    public PlaybackFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The stream that failed, when known.</summary>
    public MediaRequest? Request { get; }
}
