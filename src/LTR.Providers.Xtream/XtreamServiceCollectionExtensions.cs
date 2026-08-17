using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LTR.Providers.Xtream;

/// <summary>
/// Registers the Xtream provider with the dependency injection container.
/// </summary>
public static class XtreamServiceCollectionExtensions
{
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
                // The figures live in XtreamTimeouts because the client needs one of them too: it reads a
                // response body as a stream, which is outside this pipeline.
                options.AttemptTimeout.Timeout = XtreamTimeouts.Attempt;
                options.TotalRequestTimeout.Timeout = XtreamTimeouts.TotalRequest;
                options.CircuitBreaker.SamplingDuration = XtreamTimeouts.BreakerSampling;
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
        services.AddHttpClient<XtreamGuideSource>(client => client.Timeout = XtreamTimeouts.GuideDownload);
        services.AddTransient<IGuideSource>(provider => provider.GetRequiredService<XtreamGuideSource>());

        return services;
    }
}
