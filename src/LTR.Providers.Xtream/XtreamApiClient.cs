using System.Net;
using System.Text.Json;
using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Providers.Xtream.Dtos;
using LTR.Providers.Xtream.Json;
using Microsoft.Extensions.Logging;

namespace LTR.Providers.Xtream;

/// <summary>
/// Performs the raw HTTP calls of the Xtream player API and returns its responses as DTOs.
/// </summary>
/// <remarks>
/// Deliberately free of domain mapping and of any decision about what a response means: it turns
/// HTTP into DTOs, tolerating the shapes panels actually emit, and leaves interpretation to
/// <see cref="XtreamContentProvider"/> and <see cref="XtreamCapabilityProbe"/>.
/// </remarks>
internal sealed class XtreamApiClient
{
    /// <summary>
    /// Panels commonly answer an unrecognised request with an HTML error or maintenance page while
    /// still returning HTTP 200, so the body is inspected rather than the status code alone.
    /// </summary>
    private static readonly string[] HtmlMarkers = ["<!doctype", "<html", "<br", "<b>", "<?php"];

    private readonly HttpClient _httpClient;
    private readonly ILogger<XtreamApiClient> _logger;

    public XtreamApiClient(HttpClient httpClient, ILogger<XtreamApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Calls the action-less endpoint, which validates the credentials and reports account state.
    /// </summary>
    public async Task<XtreamAuthResponseDto> AuthenticateAsync(
        XtreamSource source,
        CancellationToken cancellationToken)
    {
        var url = XtreamEndpoints.PlayerApi(source);
        var body = await GetStringAsync(source, url, cancellationToken).ConfigureAwait(false);

        using var document = ParseJson(body, url, source);

        // A bare array — usually empty — is how several panels signal rejected credentials.
        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            XtreamLog.AuthenticationReturnedUnexpectedShape(
                _logger,
                UrlSanitizer.Sanitize(url, source),
                document.RootElement.ValueKind);

            return new XtreamAuthResponseDto();
        }

        return document.RootElement.Deserialize<XtreamAuthResponseDto>(XtreamJson.Options)
            ?? new XtreamAuthResponseDto();
    }

    public async Task<IReadOnlyList<XtreamCategoryDto>> GetCategoriesAsync(
        XtreamSource source,
        ContentKind kind,
        CancellationToken cancellationToken)
    {
        var url = XtreamEndpoints.PlayerApi(source, CategoryActionFor(kind));
        return await GetArrayAsync<XtreamCategoryDto>(source, url, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<XtreamLiveStreamDto>> GetLiveStreamsAsync(
        XtreamSource source,
        CancellationToken cancellationToken)
    {
        var url = XtreamEndpoints.PlayerApi(source, "get_live_streams");
        return await GetArrayAsync<XtreamLiveStreamDto>(source, url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports the JSON shape an action responds with, or <see langword="null"/> when it does not
    /// respond usably at all.
    /// </summary>
    /// <remarks>
    /// The shape is the signal the capability probe needs, and it differs per action: list actions
    /// answer with an array when supported, whereas a panel that does not know the action typically
    /// falls back to returning the authentication object. Returning the raw shape keeps that
    /// interpretation with the probe instead of hiding it behind a boolean here.
    /// </remarks>
    public async Task<JsonValueKind?> ProbeActionAsync(
        XtreamSource source,
        string action,
        IEnumerable<KeyValuePair<string, string>>? parameters,
        CancellationToken cancellationToken)
    {
        var url = XtreamEndpoints.PlayerApi(source, action, parameters);

        try
        {
            var body = await GetStringAsync(source, url, cancellationToken).ConfigureAwait(false);
            using var document = ParseJson(body, url, source);
            return document.RootElement.ValueKind;
        }
        catch (XtreamApiException exception)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                XtreamLog.ActionUnavailable(_logger, exception, action, UrlSanitizer.Sanitize(url, source));
            }

            return null;
        }
    }

    /// <summary>
    /// Whether an address is served at all, without downloading its body.
    /// </summary>
    /// <remarks>
    /// Used to test for <c>xmltv.php</c> and to decide between live URL shapes. A ranged GET is
    /// used rather than HEAD because panels frequently answer HEAD with 405 while serving the
    /// resource perfectly well on GET.
    /// </remarks>
    public async Task<bool> ResourceExistsAsync(XtreamSource source, Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request, source);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);

        try
        {
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            return response.StatusCode is not (HttpStatusCode.NotFound
                or HttpStatusCode.Forbidden
                or HttpStatusCode.Unauthorized
                or HttpStatusCode.MethodNotAllowed
                or HttpStatusCode.InternalServerError);
        }
        catch (HttpRequestException exception)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                XtreamLog.ProbeRequestFailed(_logger, exception, UrlSanitizer.Sanitize(url, source));
            }

            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout here means "not usable", which is the answer the probe needs.
            return false;
        }
    }

    private static string CategoryActionFor(ContentKind kind)
    {
        return kind switch
        {
            ContentKind.Live => "get_live_categories",
            ContentKind.Movie => "get_vod_categories",
            ContentKind.Series => "get_series_categories",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown content kind."),
        };
    }

    private async Task<IReadOnlyList<T>> GetArrayAsync<T>(
        XtreamSource source,
        Uri url,
        CancellationToken cancellationToken)
    {
        var body = await GetStringAsync(source, url, cancellationToken).ConfigureAwait(false);
        using var document = ParseJson(body, url, source);

        // Panels answer "nothing here" with an object, with false, or with an empty string. None of
        // those is an error worth surfacing; an empty catalogue section is the correct reading.
        if (document.RootElement.ValueKind is not JsonValueKind.Array)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                XtreamLog.ListReturnedUnexpectedShape(
                    _logger,
                    UrlSanitizer.Sanitize(url, source),
                    document.RootElement.ValueKind);
            }

            return [];
        }

        return document.RootElement.Deserialize<List<T>>(XtreamJson.Options) ?? [];
    }

    private async Task<string> GetStringAsync(XtreamSource source, Uri url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request, source);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new XtreamApiException(
                $"Could not reach the panel: {exception.Message}",
                exception)
            {
                SanitizedUrl = UrlSanitizer.Sanitize(url, source),
            };
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new XtreamApiException(
                    $"The panel answered {(int)response.StatusCode} {response.ReasonPhrase}.")
                {
                    SanitizedUrl = UrlSanitizer.Sanitize(url, source),
                };
            }

            return body;
        }
    }

    private static void ApplyHeaders(HttpRequestMessage request, XtreamSource source)
    {
        // Some panels reject agents they do not recognise, so a malformed configured value must not
        // silently degrade into the default .NET agent.
        if (!request.Headers.UserAgent.TryParseAdd(source.UserAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", source.UserAgent);
        }

        request.Headers.Accept.TryParseAdd("application/json, text/plain, */*");
    }

    private static JsonDocument ParseJson(string body, Uri url, XtreamSource source)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new XtreamApiException("The panel returned an empty response.")
            {
                SanitizedUrl = UrlSanitizer.Sanitize(url, source),
            };
        }

        if (LooksLikeHtml(body))
        {
            throw new XtreamApiException(
                "The panel returned an HTML page instead of API data. The address may be wrong, or "
                + "the panel may be blocking this client.")
            {
                SanitizedUrl = UrlSanitizer.Sanitize(url, source),
            };
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new XtreamApiException($"The panel returned malformed JSON: {exception.Message}", exception)
            {
                SanitizedUrl = UrlSanitizer.Sanitize(url, source),
            };
        }
    }

    private static bool LooksLikeHtml(string body)
    {
        var start = body.AsSpan().TrimStart();

        foreach (var marker in HtmlMarkers)
        {
            if (start.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
