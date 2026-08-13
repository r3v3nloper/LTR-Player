using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LTR.Providers.M3u;

/// <summary>
/// Registers the M3U playlist provider.
/// </summary>
public static class M3uServiceCollectionExtensions
{
    /// <summary>
    /// Generous, because a full subscription playlist is a single multi-megabyte response often served
    /// by a slow host, and there is no partial result to fall back on.
    /// </summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(3);

    public static IServiceCollection AddM3uProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services
            .AddHttpClient<M3uPlaylistLoader>(client => client.Timeout = DownloadTimeout);

        // Transient for the same reason as the Xtream registrations: the typed client they depend on
        // is transient by design, and capturing it in a singleton would pin one HttpClient forever.
        services.AddTransient<IContentProviderFactory, M3uContentProviderFactory>();
        services.AddTransient<IProviderCapabilityProbe, M3uCapabilityProbe>();

        services.AddSingleton<IStreamUrlResolver, M3uStreamUrlResolver>();

        return services;
    }
}
