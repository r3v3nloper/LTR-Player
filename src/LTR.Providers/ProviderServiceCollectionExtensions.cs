using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LTR.Providers;

/// <summary>
/// Registers the protocol-neutral provider plumbing.
/// </summary>
public static class ProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers the registry that resolves provider components by source type. Call alongside the
    /// individual provider packages; order does not matter.
    /// </summary>
    public static IServiceCollection AddProviderRegistry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Transient, because the factories and probes it resolves are themselves transient by design.
        services.TryAddTransient<IProviderRegistry, ProviderRegistry>();

        return services;
    }
}
