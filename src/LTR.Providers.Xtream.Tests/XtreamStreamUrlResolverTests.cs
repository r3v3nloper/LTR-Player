using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Providers.Xtream;

public sealed class XtreamStreamUrlResolverTests
{
    private static readonly Channel SampleChannel = new()
    {
        SourceId = 1,
        ExternalId = "4711",
        Name = "Das Erste HD",
    };

    [Fact]
    public void Supports_AcceptsXtreamSourcesOnly()
    {
        // Arrange
        var resolver = new XtreamStreamUrlResolver();
        var xtreamSource = new XtreamSourceBuilder().Build();
        var m3uSource = CreateM3uSource();

        // Act & Assert
        resolver.Supports(xtreamSource).ShouldBeTrue();
        resolver.Supports(m3uSource).ShouldBeFalse();
    }

    [Fact]
    public void ResolveLive_CarriesTheSourceUserAgentAndChannelName()
    {
        // Arrange: the agent travels with the request because panels filter on it.
        var resolver = new XtreamStreamUrlResolver();
        var source = new XtreamSourceBuilder()
            .WithCredentials("alice", "secret")
            .WithUserAgent("VLC/3.0.21 LibVLC/3.0.21")
            .WithCapabilities(new ProviderCapabilities
            {
                SupportsMpegTs = true,
                RequiresLivePathSegment = true,
                ProbedAtUtc = DateTimeOffset.UnixEpoch,
            })
            .Build();

        // Act
        var request = resolver.ResolveLive(source, SampleChannel);

        // Assert
        request.Url.AbsoluteUri.ShouldBe("http://panel.example:8080/live/alice/secret/4711.ts");
        request.UserAgent.ShouldBe("VLC/3.0.21 LibVLC/3.0.21");
        request.Format.ShouldBe(StreamFormat.MpegTs);
        request.DisplayName.ShouldBe("Das Erste HD");
    }

    [Fact]
    public void ResolveLive_ForANonXtreamSource_IsRejected()
    {
        // Arrange
        var resolver = new XtreamStreamUrlResolver();
        var m3uSource = CreateM3uSource();

        // Act
        var act = () => resolver.ResolveLive(m3uSource, SampleChannel);

        // Assert
        act.ShouldThrow<NotSupportedException>();
    }

    [Fact]
    public void ChooseStreamFormat_WhenTheSourceHasNotBeenProbed_KeepsThePreference()
    {
        // Arrange: guessing against an unknown panel is worse than honouring the user's choice.
        var source = new XtreamSourceBuilder()
            .WithPreferredFormat(StreamFormat.HlsPlaylist)
            .WithCapabilities(new ProviderCapabilities())
            .Build();

        // Act
        var format = XtreamStreamUrlResolver.ChooseStreamFormat(source);

        // Assert
        format.ShouldBe(StreamFormat.HlsPlaylist);
    }

    [Fact]
    public void ChooseStreamFormat_WhenThePreferenceIsAvailable_UsesIt()
    {
        // Arrange
        var source = new XtreamSourceBuilder()
            .WithPreferredFormat(StreamFormat.HlsPlaylist)
            .WithCapabilities(Probed(supportsMpegTs: true, supportsHls: true))
            .Build();

        // Act
        var format = XtreamStreamUrlResolver.ChooseStreamFormat(source);

        // Assert
        format.ShouldBe(StreamFormat.HlsPlaylist);
    }

    [Fact]
    public void ChooseStreamFormat_WhenHlsIsPreferredButUnavailable_FallsBackToTransportStream()
    {
        // Arrange
        var source = new XtreamSourceBuilder()
            .WithPreferredFormat(StreamFormat.HlsPlaylist)
            .WithCapabilities(Probed(supportsMpegTs: true, supportsHls: false))
            .Build();

        // Act
        var format = XtreamStreamUrlResolver.ChooseStreamFormat(source);

        // Assert
        format.ShouldBe(StreamFormat.MpegTs);
    }

    [Fact]
    public void ChooseStreamFormat_WhenOnlyHlsIsAvailable_UsesHls()
    {
        // Arrange
        var source = new XtreamSourceBuilder()
            .WithPreferredFormat(StreamFormat.MpegTs)
            .WithCapabilities(Probed(supportsMpegTs: false, supportsHls: true))
            .Build();

        // Act
        var format = XtreamStreamUrlResolver.ChooseStreamFormat(source);

        // Assert
        format.ShouldBe(StreamFormat.HlsPlaylist);
    }

    [Fact]
    public void ChooseStreamFormat_WhenThePanelClaimsNoUsableFormat_TriesThePreferenceAnyway()
    {
        // Arrange: panels under-report allowed_output_formats, so refusing to play would be wrong.
        var source = new XtreamSourceBuilder()
            .WithPreferredFormat(StreamFormat.MpegTs)
            .WithCapabilities(Probed(supportsMpegTs: false, supportsHls: false))
            .Build();

        // Act
        var format = XtreamStreamUrlResolver.ChooseStreamFormat(source);

        // Assert
        format.ShouldBe(StreamFormat.MpegTs);
    }

    private static ProviderCapabilities Probed(bool supportsMpegTs, bool supportsHls)
    {
        return new ProviderCapabilities
        {
            SupportsMpegTs = supportsMpegTs,
            SupportsHls = supportsHls,
            ProbedAtUtc = DateTimeOffset.UnixEpoch,
        };
    }

    private static M3uSource CreateM3uSource()
    {
        return new M3uSource
        {
            Name = "Playlist",
            PlaylistUrl = new Uri("http://host/list.m3u", UriKind.Absolute),
        };
    }
}
