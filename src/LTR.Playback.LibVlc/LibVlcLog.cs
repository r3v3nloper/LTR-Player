using Microsoft.Extensions.Logging;

namespace LTR.Playback.LibVlc;

internal static partial class LibVlcLog
{
    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Warning,
        Message = "LibVLC did not release the stream during shutdown. The provider may keep counting the "
            + "connection for a short while.")]
    public static partial void ShutdownStopTimedOut(ILogger logger);
}
