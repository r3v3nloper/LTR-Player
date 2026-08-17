using LTR.Playback.LibVlc;
using Microsoft.Extensions.DependencyInjection;

namespace LTR.Playback;

/// <summary>
/// Registers LibVLC-backed playback.
/// </summary>
public static class LibVlcServiceCollectionExtensions
{
    /// <summary>
    /// Registers the media engine and the session that serialises access to it.
    /// </summary>
    /// <remarks>
    /// Both are singletons, and deliberately so: a single engine means a single provider connection,
    /// which is the constraint the whole playback design is built around. The session is additionally
    /// registered under <see cref="IVlcVideoSink"/>-adjacent lookups via the engine, so the view can
    /// obtain the video surface without the rest of the application seeing LibVLC.
    /// </remarks>
    public static IServiceCollection AddLibVlcPlayback(
        this IServiceCollection services,
        Action<LibVlcOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<LibVlcOptions>();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.AddSingleton<LibVlcMediaEngine>();
        services.AddSingleton<IMediaEngine>(provider => provider.GetRequiredService<LibVlcMediaEngine>());
        services.AddSingleton<IVlcVideoSink>(provider => provider.GetRequiredService<LibVlcMediaEngine>());
        // One session behind two interfaces, resolved to the same instance. Registered as itself first so
        // the container owns it and therefore disposes it — which is what releases the provider connection
        // on the way out, and the reason the container must be disposed asynchronously.
        services.AddSingleton<PlaybackSession>();
        services.AddSingleton<IPlaybackSession>(provider => provider.GetRequiredService<PlaybackSession>());
        services.AddSingleton<IPlaybackTransport>(provider => provider.GetRequiredService<PlaybackSession>());

        return services;
    }
}
