using LTR.Core.Sources;

namespace LTR.Providers.M3u;

/// <summary>
/// Removes whatever a playlist address carries as credentials, by treating every query value as one.
/// </summary>
/// <remarks>
/// <para>
/// A playlist source holds no username or password of its own: whatever the provider issued is already
/// inside the address the user pasted, typically as <c>get.php?username=…&amp;password=…</c>. Nothing here
/// knows which parameters those are — panels differ, and a token is as common as a pair — so the rule is
/// structural rather than by name, and every value goes.
/// </para>
/// <para>
/// The path is left alone, and that is the known limit of this sanitiser. Providers do exist that put
/// credentials in path segments, but with no credentials to compare against, nothing distinguishes such a
/// segment from a route: redacting the path wholesale would remove the last diagnostic value the address
/// has. The query string is where a playlist's credentials actually live, and the userinfo component —
/// handled by the base class — is the other form that occurs.
/// </para>
/// </remarks>
internal sealed class M3uUrlSanitizer : SensitiveUrlSanitizer<M3uSource>
{
    protected override string Sanitize(string url, M3uSource source)
    {
        return RedactQueryValues(url);
    }
}
