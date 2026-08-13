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
}
