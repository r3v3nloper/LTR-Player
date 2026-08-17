namespace LTR.Providers.Xtream;

/// <summary>
/// Every time limit the Xtream client works to, in one place.
/// </summary>
/// <remarks>
/// Gathered here because two of them have to agree: the resilience pipeline bounds a request while the
/// response headers are being fetched, and the client bounds reading the body itself. The body is read as a
/// stream rather than buffered by <see cref="HttpClient"/>, which is what takes it outside the pipeline's
/// reach — so a figure kept only in the service registration would leave the download of a sixty-thousand
/// film listing bounded by nothing at all.
/// </remarks>
internal static class XtreamTimeouts
{
    /// <summary>
    /// One attempt at reaching the panel. Generous because a large subscription's channel list is a single
    /// multi-megabyte JSON response served by a frequently overloaded panel.
    /// </summary>
    public static TimeSpan Attempt => TimeSpan.FromSeconds(30);

    /// <summary>
    /// Reading one response body, which the resilience pipeline no longer covers.
    /// </summary>
    /// <remarks>
    /// The same figure as <see cref="Attempt"/>, deliberately: buffering used to put the body inside the
    /// attempt, so this keeps a stalled download failing when it always did rather than hanging until the
    /// window closes.
    /// </remarks>
    public static TimeSpan BodyRead => Attempt;

    public static TimeSpan TotalRequest => TimeSpan.FromSeconds(120);

    /// <summary>
    /// Must be at least twice <see cref="Attempt"/> for the circuit breaker to have a meaningful sample; the
    /// resilience package validates this.
    /// </summary>
    public static TimeSpan BreakerSampling => TimeSpan.FromSeconds(60);

    /// <summary>
    /// A full guide is one response of tens to hundreds of megabytes, frequently from the same overloaded
    /// host. Nothing about it can be retried usefully, so it gets one generous attempt.
    /// </summary>
    public static TimeSpan GuideDownload => TimeSpan.FromMinutes(10);
}
