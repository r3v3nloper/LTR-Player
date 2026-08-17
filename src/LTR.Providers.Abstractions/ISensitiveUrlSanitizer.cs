using LTR.Core.Sources;

namespace LTR.Providers;

/// <summary>
/// Removes credentials from an address before it reaches a log file, a console or a bug report.
/// </summary>
/// <remarks>
/// <para>
/// Where the credentials sit is protocol knowledge, and it differs: an Xtream panel repeats the username
/// and password in the query string of every API call and in the path of every stream address, while a
/// playlist carries whatever its provider chose inside a query string this application never parsed. So
/// the rule cannot be written once for all protocols — but the obligation can be, which is what this
/// interface is for.
/// </para>
/// <para>
/// Implementations sanitise on a best-effort basis and must never throw for a malformed address: the
/// caller is on a diagnostic path, and losing the message that explains a failure is worse than logging
/// an address that is redacted more heavily than necessary.
/// </para>
/// </remarks>
public interface ISensitiveUrlSanitizer
{
    bool Supports(PlaylistSource source);

    /// <summary>
    /// Returns <paramref name="url"/> as text, with everything that could be a credential replaced.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// <paramref name="source"/> is not of the protocol this implementation handles.
    /// </exception>
    string Sanitize(Uri url, PlaylistSource source);
}
