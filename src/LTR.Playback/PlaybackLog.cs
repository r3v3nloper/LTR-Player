using Microsoft.Extensions.Logging;

namespace LTR.Playback;

/// <summary>
/// Diagnostics for the playback session. Stream names are logged, never stream URLs, because those
/// contain the subscription credentials.
/// </summary>
internal static partial class PlaybackLog
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Switching playback to {Stream}.")]
    public static partial void Switching(ILogger logger, string stream);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Debug,
        Message = "Abandoning the switch to {Stream}; a newer request superseded it.")]
    public static partial void SwitchSuperseded(ILogger logger, string stream);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Error,
        Message = "The engine did not release the previous stream within {TimeoutSeconds}s. The provider may "
            + "still be counting the connection, which can lock the account out temporarily.")]
    public static partial void StopTimedOut(ILogger logger, double timeoutSeconds);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Warning,
        Message = "Releasing the previous stream failed.")]
    public static partial void StopFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "Playback of {Stream} failed to start.")]
    public static partial void PlayFailed(ILogger logger, Exception exception, string stream);
}
