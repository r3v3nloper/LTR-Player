using LTR.Core.Sources;

namespace LTR.Providers.Xtream;

/// <summary>
/// Removes credentials from addresses before they reach a log file.
/// </summary>
/// <remarks>
/// Xtream carries the username and password in the query string and, for stream URLs, in the path.
/// Diagnostic logs are exactly what users paste into forums when asking for help, so every logged
/// address goes through here first.
/// </remarks>
internal static class UrlSanitizer
{
    private const string Placeholder = "***";

    public static string Sanitize(Uri url, XtreamSource source)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(source);

        var sanitized = url.AbsoluteUri;
        sanitized = Replace(sanitized, source.Username);
        sanitized = Replace(sanitized, source.Password);
        return sanitized;
    }

    private static string Replace(string url, string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return url;
        }

        // Both forms appear: raw in path segments, percent-encoded in query strings.
        var withoutRaw = url.Replace(secret, Placeholder, StringComparison.Ordinal);
        var escaped = Uri.EscapeDataString(secret);
        return escaped == secret
            ? withoutRaw
            : withoutRaw.Replace(escaped, Placeholder, StringComparison.Ordinal);
    }
}
