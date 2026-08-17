using LTR.Core.Sources;

namespace LTR.Providers.Xtream;

/// <summary>
/// Removes a panel's credentials from an address before it reaches a log file.
/// </summary>
/// <remarks>
/// <para>
/// Xtream carries the username and password in the query string of every API call and, for stream URLs, as
/// whole path segments — so each is removed where the protocol puts it: a query value or a path segment that
/// *is* the credential. The rest of the address is left intact deliberately, because the host and
/// <c>action</c> are what make a logged address worth logging.
/// </para>
/// <para>
/// It used to replace them by value wherever they occurred, which is safe but not honest: a username of
/// <c>x</c> against <c>hd-max.org</c> logged <c>http://hd-ma***.org:8080/pla***er_api.php</c>, with the two
/// things being diagnosed redacted and the credentials no better hidden. Panels do issue two-character trial
/// usernames.
/// </para>
/// </remarks>
internal sealed class XtreamUrlSanitizer : SensitiveUrlSanitizer<XtreamSource>
{
    protected override string Sanitize(string url, XtreamSource source)
    {
        return RedactCredential(RedactCredential(url, source.Username), source.Password);
    }

    /// <summary>
    /// Removes one credential from the places this protocol puts it — and from anywhere at all if it turns
    /// out not to be in one of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fallback is what keeps precision from becoming a leak. Every address this client builds comes from
    /// <see cref="XtreamEndpoints"/>, so the structural cases cover all of them, and a redirect to a streaming
    /// node keeps the same shape. But a panel is free to answer with something else, and a credential that
    /// went unrecognised must not survive into a log because the shape was unfamiliar. So when the structural
    /// pass changed nothing, the old wholesale replacement runs instead.
    /// </para>
    /// <para>
    /// Judged per credential rather than for both together, so an address that spells one of them
    /// structurally does not exempt the other. What remains uncovered is one credential appearing *both* in
    /// its proper place and buried in something else in the same address; the alternative to accepting that
    /// is redacting every address down to uselessness, which is what this replaced.
    /// </para>
    /// </remarks>
    private static string RedactCredential(string url, string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return url;
        }

        var redacted = RedactQueryValues(url, (_, value) => IsSecret(value, secret));
        redacted = RedactPathSegments(redacted, segment => IsSecret(segment, secret));

        return string.Equals(redacted, url, StringComparison.Ordinal)
            ? Redact(url, secret)
            : redacted;
    }

    /// <summary>
    /// Whether one query value or path segment is the credential, in either form an address holds it.
    /// </summary>
    private static bool IsSecret(string candidate, string secret)
    {
        if (candidate.Length == 0)
        {
            return false;
        }

        return string.Equals(candidate, secret, StringComparison.Ordinal)
            || string.Equals(Uri.UnescapeDataString(candidate), secret, StringComparison.Ordinal);
    }
}
