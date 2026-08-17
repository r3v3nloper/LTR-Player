using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Providers;

namespace LTR.Cli;

/// <summary>
/// Prints a resolved stream address, masked unless the caller asked for it verbatim.
/// </summary>
/// <remarks>
/// Shared by the two commands that print one — the panel's <c>resolve</c> and the catalogue's
/// <c>live resolve</c> — because the rule is the same and is security-adjacent: the address is the point of
/// both commands, but it embeds a paid subscription's credentials, and console output ends up in scrollback,
/// screenshots and bug reports. What counts as a credential is the protocol's business, so the masking comes
/// from the provider layer rather than being spelled out here.
/// </remarks>
internal sealed class ResolvedAddressReport
{
    private readonly IProviderRegistry _providers;

    public ResolvedAddressReport(IProviderRegistry providers)
    {
        _providers = providers;
    }

    public void Print(MediaRequest request, PlaylistSource source, bool revealCredentials)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);

        var verbatim = request.Url.AbsoluteUri;

        var masked = revealCredentials
            ? verbatim
            : _providers.GetUrlSanitizer(source).Sanitize(request.Url, source);

        Console.WriteLine($"Format      {request.Format}");
        Console.WriteLine($"User agent  {request.UserAgent}");
        Console.WriteLine($"URL         {masked}");

        if (revealCredentials)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine(
            string.Equals(masked, verbatim, StringComparison.Ordinal)
                ? "Nothing in this address could be identified as a credential, so it is printed as it "
                    + "stands. A playlist's path is the case where that happens: with no credentials on "
                    + "record there is nothing to tell a secret segment from a route. Treat it as sensitive."
                : "Credentials are masked. Pass --reveal to print the address verbatim.");
    }
}
