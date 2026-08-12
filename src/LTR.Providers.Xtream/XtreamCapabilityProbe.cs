using System.Text.Json;
using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Providers.Xtream;

/// <summary>
/// Establishes what an individual Xtream panel supports.
/// </summary>
/// <remarks>
/// <para>
/// Panels are divergent forks of a common ancestor, so support has to be observed rather than
/// assumed. Each action is tried once and the outcome recorded on the source, which lets the rest of
/// the application treat capabilities as known facts.
/// </para>
/// <para>
/// Probes run sequentially rather than concurrently: panels are frequently overloaded and some
/// rate-limit, and this runs once when a source is added, so latency does not matter here.
/// </para>
/// </remarks>
internal sealed class XtreamCapabilityProbe : IProviderCapabilityProbe
{
    /// <summary>
    /// Arbitrary identifier used only to see whether the short-EPG action exists. Panels answer with
    /// an empty listing object for unknown identifiers, which is the signal we want, and metadata
    /// calls do not occupy a streaming connection.
    /// </summary>
    private const string ProbeStreamId = "1";

    private readonly XtreamApiClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<XtreamCapabilityProbe> _logger;

    public XtreamCapabilityProbe(
        XtreamApiClient client,
        TimeProvider timeProvider,
        ILogger<XtreamCapabilityProbe> logger)
    {
        _client = client;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool Supports(PlaylistSource source)
    {
        return source is XtreamSource;
    }

    public async Task<ProviderCapabilities> ProbeAsync(PlaylistSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source is not XtreamSource xtreamSource)
        {
            throw new NotSupportedException(
                $"{nameof(XtreamCapabilityProbe)} handles Xtream sources only, but got {source.GetType().Name}.");
        }

        var authResponse = await _client.AuthenticateAsync(xtreamSource, cancellationToken).ConfigureAwait(false);
        var account = XtreamContentProvider.MapAccount(authResponse.UserInfo, _timeProvider.GetUtcNow());

        var capabilities = new ProviderCapabilities
        {
            SupportsLive = await ListActionExistsAsync(xtreamSource, "get_live_categories", cancellationToken)
                .ConfigureAwait(false),
            SupportsVod = await ListActionExistsAsync(xtreamSource, "get_vod_categories", cancellationToken)
                .ConfigureAwait(false),
            SupportsSeries = await ListActionExistsAsync(xtreamSource, "get_series", cancellationToken)
                .ConfigureAwait(false),
            SupportsShortEpg = await ShortEpgExistsAsync(xtreamSource, cancellationToken).ConfigureAwait(false),
            SupportsXmltvEpg = await _client
                .ResourceExistsAsync(xtreamSource, XtreamEndpoints.Xmltv(xtreamSource), cancellationToken)
                .ConfigureAwait(false),

            // Stream URLs are deliberately not probed. Opening one, even with a ranged request,
            // occupies one of the very few concurrent connections the account is granted, and a
            // probe that locks the user out of their own subscription is worse than a wrong guess.
            // Current panels serve the prefixed form, and a 404 at playback time is what corrects it.
            RequiresLivePathSegment = true,

            ProbedAtUtc = _timeProvider.GetUtcNow(),
        };

        ApplyStreamFormats(capabilities, account.AllowedFormats);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            XtreamLog.CapabilitiesProbed(_logger, xtreamSource.Name, Describe(capabilities));
        }

        return capabilities;
    }

    /// <summary>
    /// Renders the probe outcome as a compact list of the features found, for the log.
    /// </summary>
    private static string Describe(ProviderCapabilities capabilities)
    {
        var features = new List<string>(7);

        AddIf(features, capabilities.SupportsLive, "live");
        AddIf(features, capabilities.SupportsVod, "vod");
        AddIf(features, capabilities.SupportsSeries, "series");
        AddIf(features, capabilities.SupportsXmltvEpg, "xmltv");
        AddIf(features, capabilities.SupportsShortEpg, "short-epg");
        AddIf(features, capabilities.SupportsMpegTs, "ts");
        AddIf(features, capabilities.SupportsHls, "hls");

        return features.Count == 0 ? "nothing" : string.Join(", ", features);

        static void AddIf(List<string> target, bool condition, string name)
        {
            if (condition)
            {
                target.Add(name);
            }
        }
    }

    /// <summary>
    /// Records which containers may be requested.
    /// </summary>
    /// <remarks>
    /// An account that reports no usable format is treated as supporting both. Panels commonly leave
    /// <c>allowed_output_formats</c> empty or list only <c>rtmp</c> while happily serving transport
    /// streams, so believing an empty list would disable playback that in fact works.
    /// </remarks>
    internal static void ApplyStreamFormats(
        ProviderCapabilities capabilities,
        IReadOnlyList<StreamFormat> allowedFormats)
    {
        if (allowedFormats.Count == 0)
        {
            capabilities.SupportsMpegTs = true;
            capabilities.SupportsHls = true;
            return;
        }

        capabilities.SupportsMpegTs = allowedFormats.Contains(StreamFormat.MpegTs);
        capabilities.SupportsHls = allowedFormats.Contains(StreamFormat.HlsPlaylist);
    }

    /// <summary>
    /// A list action counts as supported only when it answers with an array. Panels that do not know
    /// an action fall back to the authentication object, which is why the shape is what decides.
    /// </summary>
    private async Task<bool> ListActionExistsAsync(
        XtreamSource source,
        string action,
        CancellationToken cancellationToken)
    {
        var shape = await _client.ProbeActionAsync(source, action, parameters: null, cancellationToken)
            .ConfigureAwait(false);

        return shape == JsonValueKind.Array;
    }

    private async Task<bool> ShortEpgExistsAsync(XtreamSource source, CancellationToken cancellationToken)
    {
        var parameters = new KeyValuePair<string, string>[]
        {
            new("stream_id", ProbeStreamId),
            new("limit", "1"),
        };

        var shape = await _client.ProbeActionAsync(source, "get_short_epg", parameters, cancellationToken)
            .ConfigureAwait(false);

        // The listing is wrapped in an object; an array here means the panel ignored the action and
        // answered something else entirely.
        return shape == JsonValueKind.Object;
    }
}
