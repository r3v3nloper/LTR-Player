using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Providers;

namespace LTR.Cli;

/// <summary>
/// Builds the playable address for one channel of a panel addressed by URL, so it can be checked in
/// another player.
/// </summary>
/// <remarks>
/// Takes credentials rather than a stored source, which is what makes it usable before anything has been
/// imported. <c>live resolve</c> is the counterpart for a source already in the catalogue, and the only one
/// that can address a playlist — a playlist channel's address is stored, not composed from credentials.
/// </remarks>
internal sealed class ResolveCommandHandler
{
    private readonly IProviderRegistry _providers;
    private readonly ResolvedAddressReport _report;

    public ResolveCommandHandler(IProviderRegistry providers, ResolvedAddressReport report)
    {
        _providers = providers;
        _report = report;
    }

    public async Task<int> ExecuteAsync(
        XtreamSource source,
        string streamId,
        bool revealCredentials,
        bool probeFirst,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Without a probe the resolver falls back to the configured preference, which may not be what
        // this panel serves. Opt-in, because probing costs several requests.
        if (probeFirst)
        {
            source.Capabilities = await _providers.GetCapabilityProbe(source)
                .ProbeAsync(source, cancellationToken)
                .ConfigureAwait(false);
        }

        var channel = new Channel
        {
            SourceId = source.Id,
            ExternalId = streamId,
            Name = $"stream {streamId}",
        };

        var request = _providers.GetStreamUrlResolver(source).ResolveLive(source, channel);

        _report.Print(request, source, revealCredentials);

        return 0;
    }
}
