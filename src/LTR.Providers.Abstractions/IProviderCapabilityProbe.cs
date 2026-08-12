using LTR.Core.Sources;

namespace LTR.Providers;

/// <summary>
/// Determines what a specific panel actually supports.
/// </summary>
/// <remarks>
/// Run once when a source is added, and again on explicit refresh. The result is stored on the
/// source so that normal operation never has to discover capabilities by failing.
/// </remarks>
public interface IProviderCapabilityProbe
{
    bool Supports(PlaylistSource source);

    Task<ProviderCapabilities> ProbeAsync(PlaylistSource source, CancellationToken cancellationToken);
}
