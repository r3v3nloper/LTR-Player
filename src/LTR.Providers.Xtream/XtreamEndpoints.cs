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
    private const string MoviePathSegment = "movie";
    private const string SeriesPathSegment = "series";

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

    /// <summary>
    /// Builds a film's address.
    /// </summary>
    /// <param name="containerExtension">
    /// The container the film is stored in, without a leading dot. Required rather than defaulted: the
    /// extension is part of the file's identity on the panel, and choosing one here would hide the
    /// decision from the resolver that has to make it.
    /// </param>
    /// <remarks>
    /// The <c>/movie/</c> segment is not optional the way <c>/live/</c> is. Panels that serve films at
    /// all serve them under it, so there is nothing to probe.
    /// </remarks>
    public static Uri MovieStream(XtreamSource source, string streamId, string containerExtension)
    {
        return VodStream(source, MoviePathSegment, streamId, containerExtension);
    }

    /// <summary>
    /// Builds an episode's address, which is keyed by the episode's own id rather than the series'.
    /// </summary>
    public static Uri EpisodeStream(XtreamSource source, string episodeId, string containerExtension)
    {
        return VodStream(source, SeriesPathSegment, episodeId, containerExtension);
    }

    private static Uri VodStream(
        XtreamSource source,
        string pathSegment,
        string itemId,
        string containerExtension)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerExtension);

        var path = string.Concat(
            pathSegment,
            "/",
            Escape(source.Username),
            "/",
            Escape(source.Password),
            "/",
            Escape(itemId),
            ".",
            // Panels state the extension with and without its dot, and a doubled one is a 404.
            Escape(containerExtension.TrimStart('.')));

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
