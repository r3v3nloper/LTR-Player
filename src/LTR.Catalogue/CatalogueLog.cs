using Microsoft.Extensions.Logging;

namespace LTR.Catalogue;

/// <summary>
/// Diagnostic messages from the catalogue layer.
/// </summary>
/// <remarks>
/// A guide import is the one operation here that runs for minutes and can succeed while achieving
/// nothing — a guide that reads perfectly and matches no channel looks identical to a broken player from
/// the outside. These messages are what tell the two apart from a log file.
/// </remarks>
internal static partial class CatalogueLog
{
    [LoggerMessage(
        EventId = 1400,
        Level = LogLevel.Information,
        Message = "Imported the guide for {Source}: {Programmes} programmes, matched to {Matched} of "
            + "{Total} channels, in {Seconds:F1}s.")]
    public static partial void GuideImported(
        ILogger logger,
        string source,
        int programmes,
        int matched,
        int total,
        double seconds);

    [LoggerMessage(
        EventId = 1401,
        Level = LogLevel.Warning,
        Message = "The guide for {Source} was reachable but held no usable programme; the address is "
            + "probably not an XMLTV document.")]
    public static partial void GuideContainedNothing(ILogger logger, string source);

    [LoggerMessage(
        EventId = 1402,
        Level = LogLevel.Warning,
        Message = "The guide for {Source} ended mid-document; keeping the {Programmes} programmes read "
            + "before that.")]
    public static partial void GuideWasTruncated(ILogger logger, string source, int programmes);

    [LoggerMessage(
        EventId = 1403,
        Level = LogLevel.Information,
        Message = "Pruned {Programmes} finished programmes and {Channels} guide channels left empty by it.")]
    public static partial void GuidePruned(ILogger logger, int programmes, int channels);

    [LoggerMessage(
        EventId = 1404,
        Level = LogLevel.Debug,
        Message = "The guide for {Source} was imported at {ImportedAt} and is still fresh; skipping.")]
    public static partial void GuideStillFresh(ILogger logger, string source, DateTimeOffset importedAt);

    [LoggerMessage(
        EventId = 1405,
        Level = LogLevel.Information,
        Message = "Fetched the detail of {Series}: {Seasons} seasons, {Episodes} episodes.")]
    public static partial void SeriesDetailFetched(
        ILogger logger,
        string series,
        int seasons,
        int episodes);

    [LoggerMessage(
        EventId = 1406,
        Level = LogLevel.Warning,
        Message = "Could not fetch the detail of {Item} from {Source}; showing what is stored.")]
    public static partial void DetailFetchFailed(
        ILogger logger,
        Exception exception,
        string item,
        string source);

    [LoggerMessage(
        EventId = 1407,
        Level = LogLevel.Information,
        Message = "Imported {Source}: {Channels} channels, {Movies} films, {Series} series.")]
    public static partial void CatalogueImported(
        ILogger logger,
        string source,
        int channels,
        int movies,
        int series);
}
