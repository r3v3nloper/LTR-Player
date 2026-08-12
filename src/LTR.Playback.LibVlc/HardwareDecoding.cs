namespace LTR.Playback.LibVlc;

/// <summary>
/// How video decoding should be offloaded to the GPU.
/// </summary>
public enum HardwareDecoding
{
    /// <summary>Let LibVLC choose, falling back to software when no decoder fits the stream.</summary>
    Automatic = 0,

    /// <summary>Force Direct3D 11 video acceleration.</summary>
    Direct3D11 = 1,

    /// <summary>Decode in software. The reliable last resort for streams with broken headers.</summary>
    Disabled = 2,
}
