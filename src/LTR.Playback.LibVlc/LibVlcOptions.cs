using System.Globalization;

namespace LTR.Playback.LibVlc;

/// <summary>
/// Startup options handed to LibVLC.
/// </summary>
/// <remarks>
/// The defaults are chosen for IPTV rather than for local files. Live MPEG-TS from a provider
/// routinely carries discontinuous or plainly wrong timestamps, and LibVLC's default clock handling
/// reacts to that by stuttering or dropping the stream. Every value here is configurable because the
/// right setting differs per provider.
/// </remarks>
public sealed class LibVlcOptions
{
    /// <summary>
    /// Buffer held before playback starts, in milliseconds. Higher values survive jittery providers
    /// at the cost of slower channel changes.
    /// </summary>
    /// <remarks>
    /// Applies to films and episodes. Live television uses
    /// <see cref="LiveNetworkCachingMilliseconds"/> instead.
    /// </remarks>
    public int NetworkCachingMilliseconds { get; set; } = 1000;

    /// <summary>
    /// Buffer held before a live channel starts, in milliseconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lower than the on-demand figure because it is paid in full on every channel change, and it is the
    /// only part of a zap that can be shortened at all — releasing the previous stream first is required by
    /// the provider's connection limit, not by choice.
    /// </para>
    /// <para>
    /// 600 ms is a starting point, not a measured optimum, and the right value is a property of the
    /// provider rather than of the player: a panel that delivers in bursts needs more, a local one less.
    /// Set it higher if channels stutter in the first seconds; that symptom is this value being too low.
    /// </para>
    /// </remarks>
    public int LiveNetworkCachingMilliseconds { get; set; } = 600;

    /// <summary>
    /// Clock jitter tolerance in microseconds. Zero disables jitter correction, which is what makes
    /// streams with broken timestamps playable instead of stuttering.
    /// </summary>
    public int ClockJitterMicroseconds { get; set; }

    /// <summary>
    /// Whether to synchronise against the stream's clock. Disabled by default because IPTV streams
    /// frequently misreport it.
    /// </summary>
    public bool ClockSynchronisation { get; set; }

    public HardwareDecoding HardwareDecoding { get; set; } = HardwareDecoding.Automatic;

    /// <summary>
    /// Suppresses video output entirely, leaving audio and stream metadata intact.
    /// </summary>
    /// <remarks>
    /// For hosts with no window to render into, such as the verification CLI. Without this LibVLC
    /// opens a window of its own, and on Windows the Direct3D11 output then fails to allocate decoder
    /// buffers — producing a stream of h264 errors that look like a broken stream but are only a
    /// missing surface.
    /// </remarks>
    public bool DisableVideoOutput { get; set; }

    /// <summary>Raises LibVLC's own log verbosity, for diagnosing playback failures.</summary>
    /// <remarks>
    /// Affects how much LibVLC reports, not where it goes. Its output is routed into the
    /// application's logger either way, so a provider's broken stream is recorded as what it is
    /// rather than printed to whatever console happens to be attached.
    /// </remarks>
    public bool VerboseLogging { get; set; }

    /// <summary>
    /// Directory holding the native LibVLC binaries. Left unset, the loader searches next to the
    /// executable, which is where the VideoLAN.LibVLC.Windows package puts them.
    /// </summary>
    public string? NativeLibraryDirectory { get; set; }

    /// <summary>
    /// Renders these options as LibVLC command line arguments.
    /// </summary>
    public string[] ToArguments()
    {
        var arguments = new List<string>
        {
            FormattableString.Invariant($"--network-caching={NetworkCachingMilliseconds}"),
            FormattableString.Invariant($"--clock-jitter={ClockJitterMicroseconds}"),
            $"--clock-synchro={(ClockSynchronisation ? 1 : 0).ToString(CultureInfo.InvariantCulture)}",

            // The player draws its own overlay, so LibVLC must not paint a title over the video.
            "--no-video-title-show",

            // No playlist semantics are wanted: one media at a time, controlled by the session.
            "--no-sub-autodetect-file",
        };

        arguments.Add($"--avcodec-hw={ToAvcodecHwValue(HardwareDecoding)}");

        if (DisableVideoOutput)
        {
            arguments.Add("--no-video");
        }

        if (VerboseLogging)
        {
            arguments.Add("--verbose=2");
        }
        else
        {
            // Stops LibVLC writing to stderr. IPTV streams from real providers produce a constant
            // stream of decoder complaints, and printed to a console they read as application faults.
            // They are captured through the log callback instead.
            arguments.Add("--quiet");
        }

        return [.. arguments];
    }

    private static string ToAvcodecHwValue(HardwareDecoding hardwareDecoding)
    {
        return hardwareDecoding switch
        {
            HardwareDecoding.Automatic => "any",
            HardwareDecoding.Direct3D11 => "d3d11va",
            HardwareDecoding.Disabled => "none",
            _ => "any",
        };
    }
}
