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
/// <param name="output">
/// Where the lines go, injected rather than reached for. <c>Console.Out</c> in the application and a
/// <see cref="System.IO.StringWriter"/> in the tests — which is the whole reason the gate below can be held by
/// one: a decision about credentials that only a human reading scrollback could check was not being checked.
/// </param>
internal sealed class ResolvedAddressReport
{
    private readonly IProviderRegistry _providers;
    private readonly TextWriter _output;

    public ResolvedAddressReport(IProviderRegistry providers, TextWriter output)
    {
        _providers = providers;
        _output = output;
    }

    public void Print(MediaRequest request, PlaylistSource source, bool revealCredentials)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);

        var verbatim = request.Url.AbsoluteUri;

        var masked = revealCredentials
            ? verbatim
            : _providers.GetUrlSanitizer(source).Sanitize(request.Url, source);

        _output.WriteLine($"Format      {request.Format}");
        _output.WriteLine($"User agent  {request.UserAgent}");
        _output.WriteLine($"URL         {masked}");

        if (revealCredentials)
        {
            return;
        }

        _output.WriteLine();
        _output.WriteLine(
            string.Equals(masked, verbatim, StringComparison.Ordinal)
                ? "Nothing in this address could be identified as a credential, so it is printed as it "
                    + "stands. A playlist's path is the case where that happens: with no credentials on "
                    + "record there is nothing to tell a secret segment from a route. Treat it as sensitive."
                : "Credentials are masked. Pass --reveal to print the address verbatim.");
    }
}
