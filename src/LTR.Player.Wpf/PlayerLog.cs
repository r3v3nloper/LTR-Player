using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

internal static partial class PlayerLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Channel {Channel} could not be played; the provider may have taken it offline.")]
    public static partial void ChannelUnplayable(ILogger logger, Exception exception, string channel);
}
