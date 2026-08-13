using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Providers;

namespace LTR.Cli;

/// <summary>
/// Builds the playable address for one channel, so it can be checked in another player.
/// </summary>
internal sealed class ResolveCommandHandler
{
    private const string CredentialMask = "***";

    private readonly IProviderRegistry _providers;

    public ResolveCommandHandler(IProviderRegistry providers)
    {
        _providers = providers;
    }

    public async Task<int> ExecuteAsync(
        XtreamSource source,
        string streamId,
        bool revealCredentials,
        bool probeFirst,
        CancellationToken cancellationToken)
    {
        // Without a probe the resolver falls back to the configured preference, which may not be what
        // this panel serves. Opt-in, because probing costs several requests.
        if (probeFirst)
        {
            source.Capabilities = await _providers.GetCapabilityProbe(source)
                .ProbeAsync(source, cancellationToken)
                .ConfigureAwait(false);
        }

        var resolver = _providers.GetStreamUrlResolver(source);

        var channel = new Channel
        {
            SourceId = source.Id,
            ExternalId = streamId,
            Name = $"stream {streamId}",
        };

        var request = resolver.ResolveLive(source, channel);

        Console.WriteLine($"Format      {request.Format}");
        Console.WriteLine($"User agent  {request.UserAgent}");
        Console.WriteLine($"URL         {Present(request.Url, source, revealCredentials)}");

        if (!revealCredentials)
        {
            Console.WriteLine();
            Console.WriteLine("Credentials are masked. Pass --reveal to print the address verbatim.");
        }

        return 0;
    }

    /// <summary>
    /// Masks the credentials unless the caller explicitly asked for them.
    /// </summary>
    /// <remarks>
    /// The address is the point of this command, but it embeds a paid subscription's username and
    /// password in its path — and console output ends up in scrollback, screenshots and bug reports.
    /// </remarks>
    private static string Present(Uri url, XtreamSource source, bool revealCredentials)
    {
        if (revealCredentials)
        {
            return url.AbsoluteUri;
        }

        return url.AbsoluteUri
            .Replace(Uri.EscapeDataString(source.Username), CredentialMask, StringComparison.Ordinal)
            .Replace(Uri.EscapeDataString(source.Password), CredentialMask, StringComparison.Ordinal)
            .Replace(source.Username, CredentialMask, StringComparison.Ordinal)
            .Replace(source.Password, CredentialMask, StringComparison.Ordinal);
    }
}
