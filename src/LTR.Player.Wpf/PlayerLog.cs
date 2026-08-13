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
}
