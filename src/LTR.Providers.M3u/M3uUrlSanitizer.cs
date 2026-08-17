using LTR.Core.Sources;

namespace LTR.Providers.M3u;

/// <summary>
/// Removes what a playlist address carries as credentials: every query value, and — where the source's own
/// address reveals them — the path segments that are credentials too.
/// </summary>
/// <remarks>
/// <para>
/// A playlist source holds no username or password of its own: whatever the provider issued is already
/// inside the address the user pasted, typically as <c>get.php?username=…&amp;password=…</c>. Nothing here
/// knows which parameters those are — panels differ, and a token is as common as a pair — so for the address
/// being sanitised the rule is structural rather than by name, and every query value goes.
/// </para>
/// <para>
/// The path used to be left alone entirely, and that leaked: providers exist that spell the credentials as
/// path segments, so a channel address came back as
/// <c>http://host/live/alice/s3cret/101.ts</c> with nothing removed. What was missing was not a rule but a
/// fact — <b>which values are the credentials</b> — and the source has been carrying it all along, in the
/// query of its own playlist and guide addresses. Those values are therefore known secrets, and are removed
/// from a channel's path as well.
/// </para>
/// <para>
/// Matched only where such a value is a *whole* path segment, which is what makes using them safe: a
/// playlist address also carries <c>type=m3u_plus</c> and <c>output=ts</c>, and replacing "ts" wherever it
/// occurred would redact the extension off every channel — the mistake the Xtream sanitiser was corrected
/// for. Nothing is filtered out of the candidates for being short or ordinary, because over-redaction is the
/// safe direction and a whole segment matching by accident is rare.
/// </para>
/// <para>
/// **What remains uncovered:** a playlist held as a local file, whose own address is a path with no query at
/// all. Then nothing reveals the credentials and a path is again indistinguishable from a route. The CLI says
/// so rather than claiming a masking it did not perform.
/// </para>
/// </remarks>
internal sealed class M3uUrlSanitizer : SensitiveUrlSanitizer<M3uSource>
{
    protected override string Sanitize(string url, M3uSource source)
    {
        var redacted = RedactQueryValues(url);

        foreach (var secret in CredentialsKnownFrom(source))
        {
            redacted = RedactPathSegments(redacted, segment => IsSecret(segment, secret));
        }

        return redacted;
    }

    /// <summary>
    /// The values the provider issued, taken from the query of the addresses the source was configured with.
    /// </summary>
    /// <remarks>
    /// Both addresses, because a subscription's guide link carries the same credentials as its playlist and
    /// either may be the one the user pasted.
    /// </remarks>
    private static IEnumerable<string> CredentialsKnownFrom(M3uSource source)
    {
        var addresses = source.EpgUrl is { } guide
            ? new[] { source.PlaylistUrl, guide }
            : [source.PlaylistUrl];

        return addresses
            .Where(address => address.IsAbsoluteUri)
            .SelectMany(QueryValuesOf)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> QueryValuesOf(Uri address)
    {
        var query = address.Query.TrimStart('?');

        if (query.Length == 0)
        {
            return [];
        }

        return query
            .Split('&')
            .Select(pair => pair.IndexOf('=', StringComparison.Ordinal) is var separator && separator >= 0
                ? pair[(separator + 1)..]
                : pair)
            .Select(Uri.UnescapeDataString);
    }

    /// <summary>
    /// Whether one path segment is the credential, in either form an address holds it.
    /// </summary>
    private static bool IsSecret(string segment, string secret)
    {
        if (segment.Length == 0)
        {
            return false;
        }

        return string.Equals(segment, secret, StringComparison.Ordinal)
            || string.Equals(Uri.UnescapeDataString(segment), secret, StringComparison.Ordinal);
    }
}
