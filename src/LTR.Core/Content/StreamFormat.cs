namespace LTR.Core.Content;

/// <summary>
/// Container format requested from the provider for a live stream.
/// </summary>
public enum StreamFormat
{
    /// <summary>Raw MPEG transport stream. Lowest zap latency, most widely supported.</summary>
    MpegTs = 0,

    /// <summary>HLS playlist. Higher latency, but survives flaky connections better.</summary>
    HlsPlaylist = 1,
}
