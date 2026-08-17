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

    /// <summary>
    /// A full guide is one response of tens to hundreds of megabytes, frequently from the same
    /// overloaded host. Nothing about it can be retried usefully, so it gets one generous attempt.
    /// </summary>
    private static readonly TimeSpan GuideDownloadTimeout = TimeSpan.FromMinutes(10);

    public static IServiceCollection AddXtreamProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        // Stateless, and needed both as the protocol-neutral contract and by the components inside this
        // package that already hold an XtreamSource. Registered concretely and then handed to the
        // interface through a factory, so both routes reach the same instance.
        services.TryAddSingleton<XtreamUrlSanitizer>();
        services.AddSingleton<ISensitiveUrlSanitizer>(
            provider => provider.GetRequiredService<XtreamUrlSanitizer>());

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

        // Deliberately not behind the resilience pipeline above: see XtreamGuideSource. Registered
        // through a factory rather than by type, so the instance comes from the typed-client
        // registration and arrives with its configured HttpClient instead of a default one.
        services.AddHttpClient<XtreamGuideSource>(client => client.Timeout = GuideDownloadTimeout);
        services.AddTransient<IGuideSource>(provider => provider.GetRequiredService<XtreamGuideSource>());

        return services;
    }
}
