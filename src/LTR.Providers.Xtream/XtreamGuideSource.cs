using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Providers.Xtream;

/// <summary>
/// Downloads a panel's full guide from <c>xmltv.php</c>.
/// </summary>
/// <remarks>
/// Uses an <see cref="HttpClient"/> of its own rather than the one behind
/// <see cref="XtreamApiClient"/>, whose resilience pipeline caps a request at well under a minute and
/// retries it. Neither suits a download that legitimately runs for minutes and has no partial result
/// worth resuming: a retry would restart a hundred megabytes from the beginning.
/// </remarks>
internal sealed class XtreamGuideSource : IGuideSource
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<XtreamGuideSource> _logger;

    public XtreamGuideSource(HttpClient httpClient, ILogger<XtreamGuideSource> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public bool Supports(PlaylistSource source)
    {
        return source is XtreamSource;
    }

    public async Task<bool> TryReadGuideAsync(
        PlaylistSource source,
        Func<Stream, CancellationToken, Task> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(read);

        if (source is not XtreamSource xtreamSource)
        {
            throw new NotSupportedException(
                $"{nameof(XtreamGuideSource)} handles Xtream sources only, but got {source.GetType().Name}.");
        }

        // Not every panel serves a guide, and the probe has already established which. Attempting it
        // anyway would download an HTML error page and report it as a corrupt guide.
        if (xtreamSource.Capabilities.HasBeenProbed && !xtreamSource.Capabilities.SupportsXmltvEpg)
        {
            XtreamLog.GuideUnavailable(_logger, xtreamSource.Name);
            return false;
        }

        var url = XtreamEndpoints.Xmltv(xtreamSource);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (!request.Headers.UserAgent.TryParseAdd(xtreamSource.UserAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", xtreamSource.UserAgent);
        }

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new XtreamApiException($"The panel answered {(int)response.StatusCode} for its guide.")
            {
                SanitizedUrl = UrlSanitizer.Sanitize(url, xtreamSource),
            };
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            await read(stream, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }
}
