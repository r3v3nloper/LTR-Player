using System.Text;
using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Providers.Xtream;

/// <summary>
/// Single source of truth for every address the Xtream protocol uses.
/// </summary>
/// <remarks>
/// Centralised because these URLs are where panel implementations differ most, and because they
/// embed credentials in path segments — an escaping mistake here leaks or corrupts them. Kept free
/// of I/O so the rules stay under plain unit test.
/// </remarks>
internal static class XtreamEndpoints
{
    private const string PlayerApiPath = "player_api.php";
    private const string XmltvPath = "xmltv.php";
    private const string PlaylistPath = "get.php";
    private const string LivePathSegment = "live";

    /// <summary>
    /// Builds a <c>player_api.php</c> call. Omitting <paramref name="action"/> yields the
    /// authentication endpoint, which is the same URL without an action.
    /// </summary>
    public static Uri PlayerApi(
        XtreamSource source,
        string? action = null,
        IEnumerable<KeyValuePair<string, string>>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        var query = new StringBuilder();
        AppendCredentials(query, source);

        if (!string.IsNullOrWhiteSpace(action))
        {
            AppendParameter(query, "action", action);
        }

        if (parameters is not null)
        {
            foreach (var parameter in parameters)
            {
                AppendParameter(query, parameter.Key, parameter.Value);
            }
        }

        return Combine(source.BaseUrl, $"{PlayerApiPath}?{query}");
    }

    /// <summary>Builds the full-guide download address.</summary>
    public static Uri Xmltv(XtreamSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var query = new StringBuilder();
        AppendCredentials(query, source);

        return Combine(source.BaseUrl, $"{XmltvPath}?{query}");
    }

    /// <summary>
    /// Builds the M3U-Plus playlist address, used as a fallback when the player API is unavailable
    /// but the same credentials still serve a playlist.
    /// </summary>
    public static Uri Playlist(XtreamSource source, StreamFormat format)
    {
        ArgumentNullException.ThrowIfNull(source);

        var query = new StringBuilder();
        AppendCredentials(query, source);
        AppendParameter(query, "type", "m3u_plus");
        AppendParameter(query, "output", format.ToUrlExtension());

        return Combine(source.BaseUrl, $"{PlaylistPath}?{query}");
    }

    /// <summary>
    /// Builds a live stream address.
    /// </summary>
    /// <param name="useLivePathSegment">
    /// Whether to insert the <c>/live/</c> segment. Newer panels require it; older ones serve
    /// <c>/{user}/{pass}/{id}.{ext}</c> and return 404 for the prefixed form, so the capability
    /// probe decides and this method does not guess.
    /// </param>
    public static Uri LiveStream(XtreamSource source, string streamId, StreamFormat format, bool useLivePathSegment)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        var prefix = useLivePathSegment ? $"{LivePathSegment}/" : string.Empty;
        var path = string.Concat(
            prefix,
            Escape(source.Username),
            "/",
            Escape(source.Password),
            "/",
            Escape(streamId),
            ".",
            format.ToUrlExtension());

        return Combine(source.BaseUrl, path);
    }

    private static void AppendCredentials(StringBuilder query, XtreamSource source)
    {
        AppendParameter(query, "username", source.Username);
        AppendParameter(query, "password", source.Password);
    }

    private static void AppendParameter(StringBuilder query, string name, string value)
    {
        if (query.Length > 0)
        {
            query.Append('&');
        }

        query.Append(Escape(name)).Append('=').Append(Escape(value));
    }

    private static string Escape(string value)
    {
        return Uri.EscapeDataString(value);
    }

    /// <summary>
    /// Appends a relative address to the panel base, preserving any path prefix the base carries.
    /// </summary>
    /// <remarks>
    /// The trailing slash is significant: without it <see cref="Uri"/> treats the final segment of
    /// the base as a file name and replaces it, which silently drops the path prefix of panels
    /// hosted behind a reverse proxy.
    /// </remarks>
    private static Uri Combine(Uri baseUrl, string relative)
    {
        var normalized = baseUrl.AbsoluteUri.EndsWith('/')
            ? baseUrl
            : new Uri(baseUrl.AbsoluteUri + "/", UriKind.Absolute);

        return new Uri(normalized, relative);
    }
}
