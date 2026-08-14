using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

internal static partial class PlayerLog
{
    /// <remarks>
    /// Logged on every start. Which database file is in use is otherwise invisible, and answering
    /// "why does my catalogue look different here" without it means guessing from file sizes.
    /// </remarks>
    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Catalogue database: {DatabasePath}")]
    public static partial void UsingDatabase(ILogger logger, string databasePath);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Information,
        Message = "Protected {Count} stored credential(s) that predated credential protection.")]
    public static partial void CredentialsUpgraded(ILogger logger, int count);

    [LoggerMessage(
        EventId = 3008,
        Level = LogLevel.Error,
        Message = "The catalogue database could not be read and was set aside as {QuarantinedPath}; "
            + "started with an empty one.")]
    public static partial void CatalogueQuarantined(ILogger logger, string quarantinedPath);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Error,
        Message = "The stored catalogue for {Source} could not be read.")]
    public static partial void CatalogueLoadFailed(ILogger logger, Exception exception, string source);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Information,
        Message = "Loaded {SourceCount} configured source(s).")]
    public static partial void LoadedSources(ILogger logger, int sourceCount);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = "Source {Source}: {ChannelCount} channels, {CategoryCount} categories, "
            + "{FavoriteCount} favourites.")]
    public static partial void LoadedCatalogue(
        ILogger logger,
        string source,
        int channelCount,
        int categoryCount,
        int favoriteCount);

    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Channel {Channel} could not be played; the provider may have taken it offline.")]
    public static partial void ChannelUnplayable(ILogger logger, Exception exception, string channel);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Error,
        Message = "The programme guide for {Source} could not be imported.")]
    public static partial void GuideImportFailed(ILogger logger, Exception exception, string source);

    /// <remarks>
    /// Warning rather than error: the periodic refresh failing leaves stale programme titles on screen,
    /// which is a blemish and not a broken player.
    /// </remarks>
    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Warning,
        Message = "Rereading what is on now failed; the channel list keeps what it was showing.")]
    public static partial void GuideRefreshFailed(ILogger logger, Exception exception);

    /// <remarks>
    /// Warning rather than error: the film plays perfectly well without a synopsis.
    /// </remarks>
    [LoggerMessage(
        EventId = 3009,
        Level = LogLevel.Warning,
        Message = "The detail of film {Film} could not be read; showing what the listing supplied.")]
    public static partial void MovieDetailFailed(ILogger logger, Exception exception, string film);

    [LoggerMessage(
        EventId = 3010,
        Level = LogLevel.Error,
        Message = "The episodes of {Series} could not be loaded.")]
    public static partial void SeriesDetailFailed(ILogger logger, Exception exception, string series);

    /// <remarks>
    /// Warning, and swallowed at the call site. This runs while playback is being released — including on
    /// the way out of the window — and a lost resume position matters far less than a shutdown that stalls.
    /// </remarks>
    [LoggerMessage(
        EventId = 3011,
        Level = LogLevel.Warning,
        Message = "Where the viewer left {Kind} {ItemId} could not be recorded.")]
    public static partial void ProgressNotRecorded(
        ILogger logger,
        Exception exception,
        string kind,
        int itemId);

    /// <remarks>
    /// Warning rather than error, for the same reason as the guide refresh: this is a timer tick, and one
    /// that fails leaves the position it was following to the next one a few seconds later.
    /// </remarks>
    [LoggerMessage(
        EventId = 3013,
        Level = LogLevel.Warning,
        Message = "Sampling where playback has reached failed.")]
    public static partial void PlaybackSampleFailed(ILogger logger, Exception exception);

    /// <remarks>
    /// Warning rather than error, and the file is left where it is rather than replaced. Whatever is wrong
    /// with it is worth being able to look at, and the player runs perfectly well on the defaults.
    /// </remarks>
    [LoggerMessage(
        EventId = 3014,
        Level = LogLevel.Warning,
        Message = "The settings in {Path} could not be read; using the defaults.")]
    public static partial void SettingsNotRead(ILogger logger, Exception exception, string path);

    [LoggerMessage(
        EventId = 3015,
        Level = LogLevel.Warning,
        Message = "The settings could not be written to {Path}; this session's changes are lost.")]
    public static partial void SettingsNotSaved(ILogger logger, Exception exception, string path);

    [LoggerMessage(
        EventId = 3016,
        Level = LogLevel.Error,
        Message = "The settings for source {Source} could not be stored.")]
    public static partial void SourceSettingsNotSaved(ILogger logger, Exception exception, string source);

    [LoggerMessage(
        EventId = 3012,
        Level = LogLevel.Information,
        Message = "Source {Source}: {MovieCount} films, {SeriesCount} series.")]
    public static partial void LoadedVodCatalogue(
        ILogger logger,
        string source,
        int movieCount,
        int seriesCount);
}
