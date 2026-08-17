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
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="format"/> is <see cref="StreamFormat.ProgressiveFile"/>, which is not requestable,
    /// or a format this method has not been taught.
    /// </exception>
    public static string ToUrlExtension(this StreamFormat format)
    {
        return format switch
        {
            StreamFormat.MpegTs => MpegTsExtension,
            StreamFormat.HlsPlaylist => HlsExtension,

            // Stated as its own case rather than left to the fallback: a film's container is the panel's
            // choice and part of the film's address, so there is nothing to derive here and the caller is
            // holding the answer already. Falling through would have said "unknown format", which is the
            // one thing this is not.
            StreamFormat.ProgressiveFile => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "A progressive file is never requested by extension. Its container is part of the "
                + "address the panel stated — use the container extension that came with the item."),

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
