using LTR.Core.Sources;

namespace LTR.Providers.Xtream;

/// <summary>
/// Removes a panel's credentials from an address before it reaches a log file.
/// </summary>
/// <remarks>
/// Xtream carries the username and password in the query string of every API call and, for stream URLs,
/// in the path — so both are replaced wherever they occur rather than by position. The rest of the query
/// string is left intact deliberately: <c>action</c>, <c>vod_id</c> and <c>series_id</c> are what make a
/// logged address worth logging, and this protocol's secrets are known by name, so there is no need to
/// redact by suspicion the way the M3U sanitiser has to.
/// </remarks>
internal sealed class XtreamUrlSanitizer : SensitiveUrlSanitizer<XtreamSource>
{
    protected override string Sanitize(string url, XtreamSource source)
    {
        return Redact(Redact(url, source.Username), source.Password);
    }
}
