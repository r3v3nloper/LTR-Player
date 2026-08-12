namespace LTR.Core.Content;

/// <summary>
/// Maps <see cref="StreamFormat"/> onto the wire representations used by provider URLs.
/// </summary>
public static class StreamFormatExtensions
{
    private const string MpegTsExtension = "ts";
    private const string HlsExtension = "m3u8";

    /// <summary>
    /// Returns the URL path extension, without a leading dot, that requests this format.
    /// </summary>
    public static string ToUrlExtension(this StreamFormat format)
    {
        return format switch
        {
            StreamFormat.MpegTs => MpegTsExtension,
            StreamFormat.HlsPlaylist => HlsExtension,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown stream format."),
        };
    }

    /// <summary>
    /// Maps one entry of an Xtream <c>allowed_output_formats</c> array onto a known format,
    /// returning <see langword="null"/> for formats this player does not play (such as rtmp).
    /// </summary>
    public static StreamFormat? FromProviderFormatName(string? providerFormatName)
    {
        if (string.IsNullOrWhiteSpace(providerFormatName))
        {
            return null;
        }

        return providerFormatName.Trim().ToLowerInvariant() switch
        {
            MpegTsExtension => StreamFormat.MpegTs,
            HlsExtension => StreamFormat.HlsPlaylist,
            "hls" => StreamFormat.HlsPlaylist,
            _ => null,
        };
    }
}
