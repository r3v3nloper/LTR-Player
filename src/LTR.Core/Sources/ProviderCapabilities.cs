namespace LTR.Core.Sources;

/// <summary>
/// What a specific panel was observed to support.
/// </summary>
/// <remarks>
/// Xtream-compatible panels are forks of a common ancestor and diverge: some omit the series
/// endpoints, some have no <c>xmltv.php</c>, some require a <c>/live/</c> path segment and some
/// reject it. Probing once and recording the result lets the UI hide what is unavailable instead
/// of surfacing errors at the point of use.
/// </remarks>
public sealed class ProviderCapabilities
{
    public bool SupportsLive { get; set; }

    public bool SupportsVod { get; set; }

    public bool SupportsSeries { get; set; }

    /// <summary>Full guide download via <c>xmltv.php</c>.</summary>
    public bool SupportsXmltvEpg { get; set; }

    /// <summary>Per-channel now/next lookup via <c>get_short_epg</c>.</summary>
    public bool SupportsShortEpg { get; set; }

    public bool SupportsMpegTs { get; set; }

    public bool SupportsHls { get; set; }

    /// <summary>
    /// Whether live URLs need the <c>/live/</c> path segment. Older panels serve
    /// <c>/{user}/{pass}/{id}.ts</c> and 404 on the prefixed form.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="true"/>, unlike the other flags here, because this one is used
    /// before a probe has run — when adding a source, and by the headless resolver. Current panels
    /// serve the prefixed form, so the default has to be the prefixed form; the CLR's <c>false</c>
    /// would silently build the legacy shape that modern panels reject.
    /// </remarks>
    public bool RequiresLivePathSegment { get; set; } = true;

    public DateTimeOffset? ProbedAtUtc { get; set; }

    public bool HasBeenProbed => ProbedAtUtc.HasValue;
}
