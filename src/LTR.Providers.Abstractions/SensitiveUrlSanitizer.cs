using LTR.Core.Sources;

namespace LTR.Providers;

/// <summary>
/// Base for the per-protocol sanitisers: carries the redaction primitives and the one rule that holds
/// for every protocol.
/// </summary>
/// <typeparam name="TSource">The source type this sanitiser handles.</typeparam>
/// <remarks>
/// <para>
/// That one rule is the userinfo component: RFC 3986 allows <c>scheme://user:password@host/</c> for any
/// scheme, and playlist links in that form do circulate. It is applied here rather than left to each
/// implementation because it is the same rule everywhere, and because a security rule kept in two places
/// is a security rule that will eventually hold in only one of them.
/// </para>
/// <para>
/// Generic on the source type so the protocol check and its diagnostic wording exist once. The
/// derived class receives the address as text rather than as a <see cref="Uri"/>: redaction produces
/// something that is deliberately no longer a valid address, so passing a <see cref="Uri"/> down the
/// chain would mean re-parsing a string that is not meant to parse.
/// </para>
/// </remarks>
public abstract class SensitiveUrlSanitizer<TSource> : ISensitiveUrlSanitizer
    where TSource : PlaylistSource
{
    /// <summary>Stands in for anything removed. Short, and obviously not a value.</summary>
    protected const string Placeholder = "***";

    public bool Supports(PlaylistSource source)
    {
        return source is TSource;
    }

    public string Sanitize(Uri url, PlaylistSource source)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(source);

        if (source is not TSource typedSource)
        {
            throw new NotSupportedException(
                $"{GetType().Name} handles {typeof(TSource).Name} only, but got {source.GetType().Name}.");
        }

        return Sanitize(WithoutUserInfo(url), typedSource);
    }

    /// <summary>
    /// Applies this protocol's rule to <paramref name="url"/>, whose userinfo has already been removed.
    /// </summary>
    protected abstract string Sanitize(string url, TSource source);

    /// <summary>
    /// Replaces every occurrence of <paramref name="secret"/>, in both the forms an address can hold it.
    /// </summary>
    protected static string Redact(string url, string? secret)
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

    /// <summary>
    /// Replaces every query-string value while keeping the parameter names.
    /// </summary>
    /// <remarks>
    /// For a protocol whose credentials this application never parsed, nothing distinguishes the secret
    /// parameters from the harmless ones, so all of them go. The names are kept because they are the
    /// diagnostic part — which parameters a provider expects, and whether the address carries any at all.
    /// </remarks>
    protected static string RedactQueryValues(string url)
    {
        return RedactQueryValues(url, (_, _) => true);
    }

    /// <summary>
    /// Replaces the query-string values <paramref name="shouldRedact"/> accepts, by name and value.
    /// </summary>
    /// <remarks>
    /// The selective form exists for a protocol that knows its secrets: replacing them by value wherever they
    /// occur also replaces them where they are not secrets, and a two-character username turns the host and
    /// the action into <c>hd-ma***.org/pla***er_api.php</c> — removing the only reason the address was logged.
    /// </remarks>
    protected static string RedactQueryValues(string url, Func<string, string, bool> shouldRedact)
    {
        ArgumentNullException.ThrowIfNull(shouldRedact);

        var queryStart = url.IndexOf('?', StringComparison.Ordinal);

        if (queryStart < 0)
        {
            return url;
        }

        var fragmentStart = url.IndexOf('#', queryStart);
        var queryEnd = fragmentStart < 0 ? url.Length : fragmentStart;
        var query = url[(queryStart + 1)..queryEnd];
        var redacted = string.Join('&', query.Split('&').Select(pair => RedactQueryPair(pair, shouldRedact)));

        return string.Concat(url.AsSpan(0, queryStart + 1), redacted, url.AsSpan(queryEnd));
    }

    /// <summary>
    /// Replaces the path segments <paramref name="shouldRedact"/> accepts, keeping the rest of the path.
    /// </summary>
    /// <remarks>
    /// The other place a credential travels: Xtream stream addresses spell it as whole segments, as in
    /// <c>/live/{user}/{password}/1234.ts</c>. Offered only as a decision per segment, because a protocol
    /// that cannot recognise its own credentials cannot use this at all — see the M3U sanitiser, which
    /// deliberately leaves paths alone.
    /// </remarks>
    protected static string RedactPathSegments(string url, Func<string, bool> shouldRedact)
    {
        ArgumentNullException.ThrowIfNull(shouldRedact);

        var pathStart = PathStart(url);

        if (pathStart < 0)
        {
            return url;
        }

        var pathEnd = url.AsSpan(pathStart).IndexOfAny('?', '#');
        pathEnd = pathEnd < 0 ? url.Length : pathStart + pathEnd;

        var path = url[pathStart..pathEnd];
        var redacted = string.Join(
            '/',
            path.Split('/').Select(segment => shouldRedact(segment) ? Placeholder : segment));

        return string.Concat(url.AsSpan(0, pathStart), redacted, url.AsSpan(pathEnd));
    }

    /// <summary>
    /// Where the path begins, which is after the authority for an absolute address.
    /// </summary>
    private static int PathStart(string url)
    {
        var schemeEnd = url.IndexOf("//", StringComparison.Ordinal);

        return schemeEnd < 0
            ? url.IndexOf('/', StringComparison.Ordinal)
            : url.IndexOf('/', schemeEnd + 2);
    }

    /// <summary>
    /// Keeps the parameter's name and replaces its value.
    /// </summary>
    /// <remarks>
    /// A parameter with no <c>=</c> at all goes entirely when it is to be redacted: a bare query string is as
    /// likely to be a token as a flag, and there is no name to preserve.
    /// </remarks>
    private static string RedactQueryPair(string pair, Func<string, string, bool> shouldRedact)
    {
        if (pair.Length == 0)
        {
            return pair;
        }

        var separator = pair.IndexOf('=', StringComparison.Ordinal);

        if (separator < 0)
        {
            return shouldRedact(string.Empty, pair) ? Placeholder : pair;
        }

        var name = pair[..separator];
        var value = pair[(separator + 1)..];

        return shouldRedact(name, value)
            ? string.Concat(pair.AsSpan(0, separator + 1), Placeholder)
            : pair;
    }

    /// <summary>
    /// Removes the <c>user:password@</c> component, where the address has one.
    /// </summary>
    /// <remarks>
    /// Rebuilt through <see cref="UriBuilder"/> rather than by replacing the text of
    /// <see cref="Uri.UserInfo"/>, because that property and <see cref="Uri.AbsoluteUri"/> do not
    /// necessarily agree on escaping — and a replacement that silently fails to match would leak the
    /// very thing this exists to remove.
    /// </remarks>
    private static string WithoutUserInfo(Uri url)
    {
        if (!url.IsAbsoluteUri || string.IsNullOrEmpty(url.UserInfo))
        {
            return url.IsAbsoluteUri ? url.AbsoluteUri : url.OriginalString;
        }

        var builder = new UriBuilder(url)
        {
            UserName = Placeholder,
            Password = string.Empty,
        };

        return builder.Uri.AbsoluteUri;
    }
}
