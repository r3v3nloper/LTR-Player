using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LTR.Providers.Xtream;

/// <summary>
/// Diagnostic messages emitted while talking to a panel.
/// </summary>
/// <remarks>
/// Source-generated rather than written with the <c>ILogger</c> extension methods, which keeps the
/// call sites allocation-free and puts every message this component can produce in one place. Every
/// address passed in here has already been through <see cref="XtreamUrlSanitizer"/>.
/// </remarks>
internal static partial class XtreamLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Warning,
        Message = "Authentication against {Url} returned {Shape} instead of an object; treating as rejected.")]
    public static partial void AuthenticationReturnedUnexpectedShape(
        ILogger logger,
        string url,
        JsonValueKind shape);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "{Url} returned {Shape} instead of an array; treating as an empty section.")]
    public static partial void ListReturnedUnexpectedShape(ILogger logger, string url, JsonValueKind shape);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Debug,
        Message = "Action {Action} is unavailable on {Url}.")]
    public static partial void ActionUnavailable(ILogger logger, Exception exception, string action, string url);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "Probe request to {Url} failed.")]
    public static partial void ProbeRequestFailed(ILogger logger, Exception exception, string url);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = "Skipped {Count} live entries from {Source} because they carried no stream id.")]
    public static partial void SkippedChannelsWithoutStreamId(ILogger logger, int count, string source);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "Dropped {Count} decorative separator rows from {Source}; they carry a stream id but "
            + "nothing playable.")]
    public static partial void SkippedSeparatorRows(ILogger logger, int count, string source);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "Probed {Source}; supported: {Capabilities}")]
    public static partial void CapabilitiesProbed(ILogger logger, string source, string capabilities);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Information,
        Message = "{Source} was probed and serves no xmltv.php, so it has no guide to import.")]
    public static partial void GuideUnavailable(ILogger logger, string source);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Information,
        Message = "{Url} returned {Shape} instead of an object; treating as no detail available.")]
    public static partial void DetailReturnedUnexpectedShape(ILogger logger, string url, JsonValueKind shape);

    [LoggerMessage(
        EventId = 1011,
        Level = LogLevel.Warning,
        Message = "The detail response from {Url} could not be read; continuing without it.")]
    public static partial void DetailUnreadable(ILogger logger, Exception exception, string url);

    [LoggerMessage(
        EventId = 1009,
        Level = LogLevel.Warning,
        Message = "Skipped {Count} {Section} entries from {Source} because they carried no identifier.")]
    public static partial void SkippedEntriesWithoutId(
        ILogger logger,
        int count,
        string section,
        string source);

    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Information,
        Message = "The episode listing of series {Series} on {Source} arrived as {Shape}, which carries no "
            + "episodes this client can read.")]
    public static partial void EpisodeListingUnreadable(
        ILogger logger,
        string series,
        string source,
        JsonValueKind shape);
}
