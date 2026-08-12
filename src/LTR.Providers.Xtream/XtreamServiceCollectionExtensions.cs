using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LTR.Providers.Xtream;

/// <summary>
/// Registers the Xtream provider with the dependency injection container.
/// </summary>
public static class XtreamServiceCollectionExtensions
{
    /// <summary>
    /// Timeout for a single attempt. Generous because a large subscription's channel list is a
    /// single multi-megabyte JSON response served by a frequently overloaded panel.
    /// </summary>
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Must be at least twice the attempt timeout for the circuit breaker to have a meaningful
    /// sample; the resilience package validates this.
    /// </summary>
    private static readonly TimeSpan BreakerSamplingDuration = TimeSpan.FromSeconds(60);

    public static IServiceCollection AddXtreamProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services
            .AddHttpClient<XtreamApiClient>()
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = AttemptTimeout;
                options.TotalRequestTimeout.Timeout = TotalRequestTimeout;
                options.CircuitBreaker.SamplingDuration = BreakerSamplingDuration;
            });

        // Transient, because the typed client they depend on is transient by design: capturing it in
        // a singleton would pin one HttpClient forever and defeat handler rotation.
        services.AddTransient<IContentProviderFactory, XtreamContentProviderFactory>();
        services.AddTransient<IProviderCapabilityProbe, XtreamCapabilityProbe>();

        // Stateless and free of I/O, so a single instance is enough.
        services.AddSingleton<IStreamUrlResolver, XtreamStreamUrlResolver>();

        return services;
    }
}
