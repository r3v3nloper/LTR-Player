using Microsoft.Extensions.Logging;

namespace LTR.Playback.LibVlc;

internal static partial class LibVlcLog
{
    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Warning,
        Message = "LibVLC {Module}: {Detail}")]
    public static partial void EngineWarning(ILogger logger, string module, string detail);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Debug,
        Message = "LibVLC {Module}: {Detail}")]
    public static partial void EngineDetail(ILogger logger, string module, string detail);

    [LoggerMessage(
        EventId = 2103,
        Level = LogLevel.Information,
        Message = "{Stream} cannot be positioned, so it starts from the beginning rather than resuming.")]
    public static partial void ResumeNotSeekable(ILogger logger, string stream);

    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Warning,
        Message = "LibVLC did not release the stream during shutdown. The provider may keep counting the "
            + "connection for a short while.")]
    public static partial void ShutdownStopTimedOut(ILogger logger);
}
