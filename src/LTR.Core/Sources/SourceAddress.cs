namespace LTR.Core.Sources;

/// <summary>
/// Interprets the address a user supplies for a source.
/// </summary>
/// <remarks>
/// Shared by every entry point that accepts one, so the desktop player and the command line tool
/// cannot disagree about what counts as a valid address or what a source ends up called.
/// </remarks>
public static class SourceAddress
{
    /// <summary>
    /// Accepts a web address or an existing local file, which is how playlists arrive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scheme is checked rather than assumed. <see cref="Uri.TryCreate(string, UriKind, out Uri)"/>
    /// is far more permissive than it looks: <c>host:8080</c> parses as an absolute URI with the scheme
    /// <c>host</c>, so accepting anything that parses would take a half-typed address and only fail
    /// later, when the source is first used.
    /// </para>
    /// <para>
    /// Existence is likewise checked after parsing, not before. A Windows path parses as an absolute
    /// <c>file:</c> URI, so a separate path branch placed after the URI attempt would never be reached.
    /// </para>
    /// </remarks>
    public static bool TryParse(string? value, out Uri address)
    {
        if (!TryParseAbsolute(value, out var parsed))
        {
            address = null!;
            return false;
        }

        if (parsed.IsFile)
        {
            if (!File.Exists(parsed.LocalPath))
            {
                address = null!;
                return false;
            }

            address = parsed;
            return true;
        }

        return AcceptIfWeb(parsed, out address);
    }

    /// <summary>
    /// Accepts a web address only, which is what a panel endpoint has to be.
    /// </summary>
    public static bool TryParseWebAddress(string? value, out Uri address)
    {
        if (!TryParseAbsolute(value, out var parsed))
        {
            address = null!;
            return false;
        }

        return AcceptIfWeb(parsed, out address);
    }

    private static bool TryParseAbsolute(string? value, out Uri parsed)
    {
        var trimmed = value?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            parsed = null!;
            return false;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out parsed!);
    }

    private static bool AcceptIfWeb(Uri parsed, out Uri address)
    {
        if (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
        {
            address = parsed;
            return true;
        }

        address = null!;
        return false;
    }

    /// <summary>
    /// A short label for an address, used as the default name of a source.
    /// </summary>
    public static string Describe(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return address.IsFile ? Path.GetFileName(address.LocalPath) : address.Host;
    }
}
