namespace LTR.Core.Content;

/// <summary>
/// Container format a stream is delivered in.
/// </summary>
public enum StreamFormat
{
    /// <summary>Raw MPEG transport stream. Lowest zap latency, most widely supported.</summary>
    MpegTs = 0,

    /// <summary>HLS playlist. Higher latency, but survives flaky connections better.</summary>
    HlsPlaylist = 1,

    /// <summary>
    /// A complete file served over HTTP, such as an <c>mp4</c> film or episode.
    /// </summary>
    /// <remarks>
    /// Recorded rather than requested: the container a film is stored in is the panel's choice and part
    /// of its address, so unlike the two above this one is never negotiated. It is distinguished because
    /// such a stream has a known length and can be seeked, which is what makes resuming possible — and
    /// because labelling a film as a transport stream in the log would simply be false.
    /// </remarks>
    ProgressiveFile = 2,
}
