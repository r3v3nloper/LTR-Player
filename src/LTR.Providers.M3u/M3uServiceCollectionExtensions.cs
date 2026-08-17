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

    /// <summary>
    /// Longer still than the playlist timeout: a guide is an order of magnitude larger than the
    /// playlist that points at it.
    /// </summary>
    private static readonly TimeSpan GuideDownloadTimeout = TimeSpan.FromMinutes(10);

    public static IServiceCollection AddM3uProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        // Singleton so the provider and the capability probe, which are resolved separately, share it.
        // It holds parsed playlists only — no HttpClient — so it does not pin a message handler.
        services.TryAddSingleton<M3uPlaylistCache>();

        // Stateless, and reached two ways: directly by the provider that logs a failed playlist fetch,
        // and through the protocol-neutral contract by callers that hold only a PlaylistSource. A factory
        // for the second, so both routes reach the same instance.
        services.TryAddSingleton<M3uUrlSanitizer>();
        services.AddSingleton<ISensitiveUrlSanitizer>(
            provider => provider.GetRequiredService<M3uUrlSanitizer>());

        services
            .AddHttpClient<M3uPlaylistLoader>(client => client.Timeout = DownloadTimeout);

        // Transient for the same reason as the Xtream registrations: the typed client they depend on
        // is transient by design, and capturing it in a singleton would pin one HttpClient forever.
        services.AddTransient<IContentProviderFactory, M3uContentProviderFactory>();
        services.AddTransient<IProviderCapabilityProbe, M3uCapabilityProbe>();

        services.AddSingleton<IStreamUrlResolver, M3uStreamUrlResolver>();

        // Its own client, for the same reason the playlist loader has one: a guide is a single large
        // response that no retry can improve. Registered through a factory so the instance comes from
        // the typed-client registration rather than being activated with a default HttpClient.
        services.AddHttpClient<M3uGuideSource>(client => client.Timeout = GuideDownloadTimeout);
        services.AddTransient<IGuideSource>(provider => provider.GetRequiredService<M3uGuideSource>());

        return services;
    }
}
