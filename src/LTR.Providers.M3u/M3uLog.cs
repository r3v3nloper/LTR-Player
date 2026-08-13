using Microsoft.Extensions.Logging;

namespace LTR.Providers.M3u;

internal static partial class M3uLog
{
    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Warning,
        Message = "The playlist for {Source} could not be retrieved.")]
    public static partial void PlaylistUnreachable(ILogger logger, Exception exception, string source);

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Information,
        Message = "Dropped {Count} decorative separator rows from {Source}.")]
    public static partial void SkippedSeparatorRows(ILogger logger, int count, string source);

    [LoggerMessage(
        EventId = 1202,
        Level = LogLevel.Warning,
        Message = "The playlist for {Source} declared {Count} entries that could not be used; they had "
            + "no address, no name, or an address that could not be parsed.")]
    public static partial void SkippedMalformedEntries(ILogger logger, int count, string source);
}
