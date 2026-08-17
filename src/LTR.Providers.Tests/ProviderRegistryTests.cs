using LTR.Core.Sources;
using LTR.Providers.M3u;
using LTR.Providers.Xtream;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LTR.Providers;

/// <summary>
/// Resolves provider components the way the applications do: through a real container with both protocol
/// packages registered.
/// </summary>
/// <remarks>
/// The sanitisers themselves are internal to their packages, so these tests name neither. They ask the
/// registry for the one that handles a source and then check what came back by what it does to an
/// address, which is also the only thing a caller can rely on.
/// </remarks>
public sealed class ProviderRegistryTests
{
    [Fact]
    public void GetUrlSanitizer_ForAPanel_RemovesTheCredentialsItsAddressesCarry()
    {
        // Arrange
        using var container = CreateProviderLayer();
        var registry = container.GetRequiredService<IProviderRegistry>();
        var source = PanelSource();
        var url = new Uri("http://panel.example:8080/player_api.php?username=alice&password=s3cret");

        // Act
        var sanitized = registry.GetUrlSanitizer(source).Sanitize(url, source);

        // Assert
        sanitized.ShouldNotContain("alice");
        sanitized.ShouldNotContain("s3cret");
    }

    [Fact]
    public void GetUrlSanitizer_ForAPlaylist_RemovesTheQueryValuesItsAddressCarries()
    {
        // Arrange: the protocol whose credentials nothing here can name, which is why it needs a
        // sanitiser of its own rather than the panel's.
        using var container = CreateProviderLayer();
        var registry = container.GetRequiredService<IProviderRegistry>();
        var source = PlaylistSource("http://host/get.php?username=alice&password=s3cret");

        // Act
        var sanitized = registry.GetUrlSanitizer(source).Sanitize(source.PlaylistUrl, source);

        // Assert
        sanitized.ShouldBe("http://host/get.php?username=***&password=***");
    }

    [Fact]
    public void GetUrlSanitizer_ForEachProtocol_ResolvesADifferentImplementation()
    {
        // Arrange: with several registered, injecting one singly would silently yield whichever came
        // last — the mistake the registry exists to prevent.
        using var container = CreateProviderLayer();
        var registry = container.GetRequiredService<IProviderRegistry>();
        var panel = PanelSource();
        var playlist = PlaylistSource("http://host/get.php");

        // Act
        var forPanel = registry.GetUrlSanitizer(panel);
        var forPlaylist = registry.GetUrlSanitizer(playlist);

        // Assert
        forPanel.ShouldNotBeSameAs(forPlaylist);
        forPanel.Supports(playlist).ShouldBeFalse();
        forPlaylist.Supports(panel).ShouldBeFalse();
    }

    [Fact]
    public void GetUrlSanitizer_WithNoProtocolPackageRegistered_NamesWhatIsMissing()
    {
        // Arrange: the realistic cause is a provider package left out of the service registration, and
        // that is a diagnosis the caller cannot make from a bare "not supported".
        var services = new ServiceCollection();
        services.AddProviderRegistry();

        using var container = services.BuildServiceProvider();
        var registry = container.GetRequiredService<IProviderRegistry>();

        // Act
        var resolve = () => registry.GetUrlSanitizer(PlaylistSource("http://host/get.php"));

        // Assert
        var exception = resolve.ShouldThrow<NotSupportedException>();
        exception.Message.ShouldContain("url sanitizer");
        exception.Message.ShouldContain(nameof(M3uSource));
    }

    /// <summary>Composes the provider layer as both applications do.</summary>
    private static ServiceProvider CreateProviderLayer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProviderRegistry();
        services.AddXtreamProvider();
        services.AddM3uProvider();

        return services.BuildServiceProvider();
    }

    private static XtreamSource PanelSource()
    {
        return new XtreamSource
        {
            Name = "Panel",
            BaseUrl = new Uri("http://panel.example:8080"),
            Username = "alice",
            Password = "s3cret",
        };
    }

    private static M3uSource PlaylistSource(string playlistUrl)
    {
        return new M3uSource { Name = "Playlist", PlaylistUrl = new Uri(playlistUrl) };
    }
}
