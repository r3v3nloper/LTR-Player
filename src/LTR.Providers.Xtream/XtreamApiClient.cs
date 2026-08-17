using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Text;
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

    private static readonly byte[] Utf8ByteOrderMark = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// How much of the response is examined before parsing begins.
    /// </summary>
    /// <remarks>
    /// Enough for a byte-order mark, any leading whitespace and the longest marker above, with room to spare.
    /// It is a peek and not a read: none of it is consumed, so the parser still receives the whole document.
    /// </remarks>
    private const int PeekLength = 512;

    private readonly HttpClient _httpClient;
    private readonly XtreamUrlSanitizer _urlSanitizer;
    private readonly ILogger<XtreamApiClient> _logger;

    public XtreamApiClient(
        HttpClient httpClient,
        XtreamUrlSanitizer urlSanitizer,
        ILogger<XtreamApiClient> logger)
    {
        _httpClient = httpClient;
        _urlSanitizer = urlSanitizer;
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

        using var document = await GetJsonAsync(source, url, cancellationToken).ConfigureAwait(false);

        // A bare array — usually empty — is how several panels signal rejected credentials.
        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            XtreamLog.AuthenticationReturnedUnexpectedShape(
                _logger,
                _urlSanitizer.Sanitize(url, source),
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

    public async Task<IReadOnlyList<XtreamVodStreamDto>> GetVodStreamsAsync(
        XtreamSource source,
        CancellationToken cancellationToken)
    {
        var url = XtreamEndpoints.PlayerApi(source, "get_vod_streams");
        return await GetArrayAsync<XtreamVodStreamDto>(source, url, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<XtreamSeriesDto>> GetSeriesAsync(
        XtreamSource source,
        CancellationToken cancellationToken)
    {
        var url = XtreamEndpoints.PlayerApi(source, "get_series");
        return await GetArrayAsync<XtreamSeriesDto>(source, url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one film's extended information, or <see langword="null"/> when the panel has none.
    /// </summary>
    public Task<XtreamVodInfoResponseDto?> GetVodInfoAsync(
        XtreamSource source,
        string vodId,
        CancellationToken cancellationToken)
    {
        var url = XtreamEndpoints.PlayerApi(source, "get_vod_info", [new("vod_id", vodId)]);
        return GetObjectAsync<XtreamVodInfoResponseDto>(source, url, cancellationToken);
    }

    /// <summary>
    /// Reads one series' seasons and episodes, or <see langword="null"/> when the panel has none.
    /// </summary>
    public Task<XtreamSeriesInfoResponseDto?> GetSeriesInfoAsync(
        XtreamSource source,
        string seriesId,
        CancellationToken cancellationToken)
    {
        var url = XtreamEndpoints.PlayerApi(source, "get_series_info", [new("series_id", seriesId)]);
        return GetObjectAsync<XtreamSeriesInfoResponseDto>(source, url, cancellationToken);
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
            using var document = await GetJsonAsync(source, url, cancellationToken).ConfigureAwait(false);
            return document.RootElement.ValueKind;
        }
        catch (XtreamApiException exception)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                XtreamLog.ActionUnavailable(_logger, exception, action, _urlSanitizer.Sanitize(url, source));
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
                XtreamLog.ProbeRequestFailed(_logger, exception, _urlSanitizer.Sanitize(url, source));
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
        using var document = await GetJsonAsync(source, url, cancellationToken).ConfigureAwait(false);

        // Panels answer "nothing here" with an object, with false, or with an empty string. None of
        // those is an error worth surfacing; an empty catalogue section is the correct reading.
        if (document.RootElement.ValueKind is not JsonValueKind.Array)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                XtreamLog.ListReturnedUnexpectedShape(
                    _logger,
                    _urlSanitizer.Sanitize(url, source),
                    document.RootElement.ValueKind);
            }

            return [];
        }

        return document.RootElement.Deserialize<List<T>>(XtreamJson.Options) ?? [];
    }

    /// <summary>
    /// Reads a detail endpoint, which answers with a single object rather than a list.
    /// </summary>
    /// <remarks>
    /// A panel asked for an item it does not have answers with the authentication object, with
    /// <c>false</c>, or with an empty array — none of which is an error worth surfacing, and all of
    /// which mean the same thing: no detail. Hence <see langword="null"/> rather than an exception.
    /// </remarks>
    private async Task<T?> GetObjectAsync<T>(XtreamSource source, Uri url, CancellationToken cancellationToken)
        where T : class
    {
        using var document = await GetJsonAsync(source, url, cancellationToken).ConfigureAwait(false);

        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                XtreamLog.DetailReturnedUnexpectedShape(
                    _logger,
                    _urlSanitizer.Sanitize(url, source),
                    document.RootElement.ValueKind);
            }

            return null;
        }

        try
        {
            return document.RootElement.Deserialize<T>(XtreamJson.Options);
        }
        catch (JsonException exception)
        {
            // A shape the tolerant converters do not cover. For a detail call the graceful reading is
            // "no detail": the film or series still plays, it simply has no synopsis. Failing here would
            // instead make opening its page an error.
            XtreamLog.DetailUnreadable(_logger, exception, _urlSanitizer.Sanitize(url, source));
            return null;
        }
    }

    /// <summary>
    /// Fetches one address and parses its response as JSON.
    /// </summary>
    /// <remarks>
    /// The body is streamed rather than read into a <see cref="string"/> first. That string was the larger of
    /// two multi-megabyte copies — UTF-16, so twice the size of the response — and the film listing of the
    /// subscription this was built against runs to 66,447 entries. What it costs is that the body arrives
    /// outside the resilience pipeline, which is why the read carries a deadline of its own.
    /// </remarks>
    private async Task<JsonDocument> GetJsonAsync(
        XtreamSource source,
        Uri url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request, source);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new XtreamApiException(
                $"Could not reach the panel: {exception.Message}",
                exception)
            {
                SanitizedUrl = _urlSanitizer.Sanitize(url, source),
            };
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new XtreamApiException(
                    $"The panel answered {(int)response.StatusCode} {response.ReasonPhrase}.")
                {
                    SanitizedUrl = _urlSanitizer.Sanitize(url, source),
                };
            }

            // Bounded here because reading a streamed body is no longer the pipeline's business: a panel that
            // sends its headers and then stalls would otherwise hold the import until the window closes.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(XtreamTimeouts.BodyRead);

            var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);

            await using (body.ConfigureAwait(false))
            {
                return await ReadJsonAsync(body, url, source, deadline.Token).ConfigureAwait(false);
            }
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

    /// <summary>
    /// Reads a response body as JSON, recognising the two things panels answer with that are not.
    /// </summary>
    /// <remarks>
    /// The emptiness and HTML checks used to run against the whole body as text. They only ever needed its
    /// first bytes, and a <see cref="PipeReader"/> is what makes looking at those possible without consuming
    /// them: nothing is advanced past, so the parser still sees the document from its first byte.
    /// </remarks>
    private async Task<JsonDocument> ReadJsonAsync(
        Stream body,
        Uri url,
        XtreamSource source,
        CancellationToken cancellationToken)
    {
        var reader = PipeReader.Create(body);

        try
        {
            var peeked = await reader.ReadAtLeastAsync(PeekLength, cancellationToken).ConfigureAwait(false);
            var start = SkipByteOrderMark(peeked.Buffer);
            var beginning = Decode(peeked.Buffer.Slice(start));

            if (string.IsNullOrWhiteSpace(beginning) && peeked.IsCompleted)
            {
                throw new XtreamApiException("The panel returned an empty response.")
                {
                    SanitizedUrl = _urlSanitizer.Sanitize(url, source),
                };
            }

            if (LooksLikeHtml(beginning))
            {
                throw new XtreamApiException(
                    "The panel returned an HTML page instead of API data. The address may be wrong, or "
                    + "the panel may be blocking this client.")
                {
                    SanitizedUrl = _urlSanitizer.Sanitize(url, source),
                };
            }

            // Only the byte-order mark is consumed. Not for the parser's sake — JsonDocument's stream
            // overloads skip one themselves, which a mutation of this line proved — but for the two checks
            // above: a mark is not whitespace to .NET, so it would sit in front of "<html" and stop an HTML
            // error page being recognised as one. Everything else is left for the parser.
            reader.AdvanceTo(start);

            try
            {
                return await JsonDocument
                    .ParseAsync(reader.AsStream(leaveOpen: true), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                throw new XtreamApiException(
                    $"The panel returned malformed JSON: {exception.Message}",
                    exception)
                {
                    SanitizedUrl = _urlSanitizer.Sanitize(url, source),
                };
            }
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns where the content starts, which is after a byte-order mark when the panel wrote one.
    /// </summary>
    /// <remarks>
    /// Panels are PHP files, and one saved with a mark emits it ahead of its response. What needs the mark
    /// out of the way is the inspection rather than the parsing: <see cref="char.IsWhiteSpace(char)"/> is
    /// false for it, so it would hide an HTML page behind a character no check accounts for.
    /// </remarks>
    private static SequencePosition SkipByteOrderMark(ReadOnlySequence<byte> buffer)
    {
        if (buffer.Length < Utf8ByteOrderMark.Length)
        {
            return buffer.Start;
        }

        Span<byte> first = stackalloc byte[Utf8ByteOrderMark.Length];
        buffer.Slice(0, Utf8ByteOrderMark.Length).CopyTo(first);

        return first.SequenceEqual(Utf8ByteOrderMark)
            ? buffer.GetPosition(Utf8ByteOrderMark.Length)
            : buffer.Start;
    }

    /// <summary>Reads the first <see cref="PeekLength"/> bytes as text, which is all the checks look at.</summary>
    private static string Decode(ReadOnlySequence<byte> buffer)
    {
        var length = (int)Math.Min(buffer.Length, PeekLength);
        Span<byte> beginning = stackalloc byte[PeekLength];
        buffer.Slice(0, length).CopyTo(beginning);

        return Encoding.UTF8.GetString(beginning[..length]);
    }

    private static bool LooksLikeHtml(string beginning)
    {
        var start = beginning.AsSpan().TrimStart();

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
