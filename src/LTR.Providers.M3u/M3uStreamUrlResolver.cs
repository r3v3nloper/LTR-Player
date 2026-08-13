using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;

namespace LTR.Providers.M3u;

/// <summary>
/// Hands back the address a playlist already stated for a channel.
/// </summary>
/// <remarks>
/// Unlike Xtream, there is nothing to construct here: the playlist supplies the complete URL per
/// entry. The only judgement is which container it points at, and that is read from the address.
/// </remarks>
internal sealed class M3uStreamUrlResolver : IStreamUrlResolver
{
    private const string HlsExtension = ".m3u8";

    public bool Supports(PlaylistSource source)
    {
        return source is M3uSource;
    }

    public MediaRequest ResolveLive(PlaylistSource source, Channel channel)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(channel);

        if (source is not M3uSource)
        {
            throw new NotSupportedException(
                $"{nameof(M3uStreamUrlResolver)} handles M3U sources only, but got {source.GetType().Name}.");
        }

        if (string.IsNullOrWhiteSpace(channel.StreamUrl)
            || !Uri.TryCreate(channel.StreamUrl, UriKind.Absolute, out var url))
        {
            throw new NotSupportedException(
                $"The channel '{channel.Name}' carries no usable address. A playlist entry must state "
                + "one, so this indicates the stored catalogue is stale or was imported by another "
                + "provider.");
        }

        return new MediaRequest(url, source.UserAgent, DetectFormat(url), channel.Name);
    }

    /// <summary>
    /// Reads the container from the address.
    /// </summary>
    /// <remarks>
    /// A playlist extension means HLS; everything else is treated as a transport stream, which is what
    /// providers serve by default and what extensionless addresses turn out to be.
    /// </remarks>
    private static StreamFormat DetectFormat(Uri url)
    {
        return url.AbsolutePath.EndsWith(HlsExtension, StringComparison.OrdinalIgnoreCase)
            ? StreamFormat.HlsPlaylist
            : StreamFormat.MpegTs;
    }
}
