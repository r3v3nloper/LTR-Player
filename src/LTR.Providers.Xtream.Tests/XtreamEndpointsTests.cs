using LTR.Core.Content;
using LTR.TestSupport;

namespace LTR.Providers.Xtream;

public sealed class XtreamEndpointsTests
{
    [Fact]
    public void PlayerApi_WithoutAction_TargetsTheAuthenticationEndpoint()
    {
        // Arrange
        var source = new XtreamSourceBuilder().WithCredentials("alice", "secret").Build();

        // Act
        var url = XtreamEndpoints.PlayerApi(source);

        // Assert
        url.AbsoluteUri.ShouldBe("http://panel.example:8080/player_api.php?username=alice&password=secret");
    }

    [Fact]
    public void PlayerApi_WithAction_AppendsTheAction()
    {
        // Arrange
        var source = new XtreamSourceBuilder().WithCredentials("alice", "secret").Build();

        // Act
        var url = XtreamEndpoints.PlayerApi(source, "get_live_streams");

        // Assert
        url.AbsoluteUri.ShouldBe(
            "http://panel.example:8080/player_api.php?username=alice&password=secret&action=get_live_streams");
    }

    [Fact]
    public void PlayerApi_WithParameters_AppendsThemAfterTheAction()
    {
        // Arrange
        var source = new XtreamSourceBuilder().WithCredentials("alice", "secret").Build();
        var parameters = new KeyValuePair<string, string>[]
        {
            new("stream_id", "42"),
            new("limit", "4"),
        };

        // Act
        var url = XtreamEndpoints.PlayerApi(source, "get_short_epg", parameters);

        // Assert
        url.AbsoluteUri.ShouldBe(
            "http://panel.example:8080/player_api.php?username=alice&password=secret"
            + "&action=get_short_epg&stream_id=42&limit=4");
    }

    [Fact]
    public void PlayerApi_WithReservedCharactersInCredentials_EscapesThem()
    {
        // Arrange: credentials containing characters that would otherwise terminate the query.
        var source = new XtreamSourceBuilder().WithCredentials("a&b=c", "p@ss word+1").Build();

        // Act
        var url = XtreamEndpoints.PlayerApi(source);

        // Assert
        url.Query.ShouldBe("?username=a%26b%3Dc&password=p%40ss%20word%2B1");
    }

    [Fact]
    public void PlayerApi_WhenTheBaseUrlCarriesAPathPrefix_PreservesIt()
    {
        // Arrange: panels behind a reverse proxy are commonly mounted under a sub-path.
        var source = new XtreamSourceBuilder().WithBaseUrl("http://host/iptv-panel").Build();

        // Act
        var url = XtreamEndpoints.PlayerApi(source);

        // Assert
        url.AbsolutePath.ShouldBe("/iptv-panel/player_api.php");
    }

    [Fact]
    public void PlayerApi_WhenTheBaseUrlAlreadyEndsInASlash_DoesNotDoubleIt()
    {
        // Arrange
        var source = new XtreamSourceBuilder().WithBaseUrl("http://host:8080/").Build();

        // Act
        var url = XtreamEndpoints.PlayerApi(source);

        // Assert
        url.AbsolutePath.ShouldBe("/player_api.php");
    }

    [Theory]
    [InlineData(StreamFormat.MpegTs, "ts")]
    [InlineData(StreamFormat.HlsPlaylist, "m3u8")]
    public void LiveStream_UsesTheExtensionMatchingTheFormat(StreamFormat format, string expectedExtension)
    {
        // Arrange
        var source = new XtreamSourceBuilder().WithCredentials("alice", "secret").Build();

        // Act
        var url = XtreamEndpoints.LiveStream(source, "1234", format, useLivePathSegment: true);

        // Assert
        url.AbsoluteUri.ShouldBe($"http://panel.example:8080/live/alice/secret/1234.{expectedExtension}");
    }

    [Fact]
    public void LiveStream_WithoutTheLiveSegment_OmitsIt()
    {
        // Arrange: older panels return 404 for the prefixed form.
        var source = new XtreamSourceBuilder().WithCredentials("alice", "secret").Build();

        // Act
        var url = XtreamEndpoints.LiveStream(source, "1234", StreamFormat.MpegTs, useLivePathSegment: false);

        // Assert
        url.AbsoluteUri.ShouldBe("http://panel.example:8080/alice/secret/1234.ts");
    }

    [Fact]
    public void LiveStream_WithASlashInThePassword_KeepsItEscapedSoThePathStaysIntact()
    {
        // Arrange: an unescaped slash would introduce an extra path segment and break the URL.
        var source = new XtreamSourceBuilder().WithCredentials("alice", "a/b").Build();

        // Act
        var url = XtreamEndpoints.LiveStream(source, "7", StreamFormat.MpegTs, useLivePathSegment: true);

        // Assert
        url.AbsoluteUri.ShouldBe("http://panel.example:8080/live/alice/a%2Fb/7.ts");
        url.Segments.Length.ShouldBe(5, "scheme root, live, username, password and stream file");
    }

    [Fact]
    public void LiveStream_WithoutAStreamId_IsRejected()
    {
        // Arrange
        var source = new XtreamSourceBuilder().Build();

        // Act
        var act = () => XtreamEndpoints.LiveStream(source, " ", StreamFormat.MpegTs, useLivePathSegment: true);

        // Assert
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void MovieStream_UsesTheMovieSegmentAndTheStatedContainer()
    {
        // Arrange
        var source = new XtreamSourceBuilder().WithCredentials("alice", "secret").Build();

        // Act
        var url = XtreamEndpoints.MovieStream(source, "8412", "mkv");

        // Assert
        url.AbsoluteUri.ShouldBe("http://panel.example:8080/movie/alice/secret/8412.mkv");
    }

    [Fact]
    public void EpisodeStream_UsesTheSeriesSegment()
    {
        // Arrange
        var source = new XtreamSourceBuilder().WithCredentials("alice", "secret").Build();

        // Act
        var url = XtreamEndpoints.EpisodeStream(source, "1001", "mp4");

        // Assert
        url.AbsoluteUri.ShouldBe("http://panel.example:8080/series/alice/secret/1001.mp4");
    }

    [Fact]
    public void MovieStream_WithADottedContainer_DoesNotDoubleTheDot()
    {
        // Arrange: panels state the extension both ways, and a doubled dot is a 404.
        var source = new XtreamSourceBuilder().WithCredentials("alice", "secret").Build();

        // Act
        var url = XtreamEndpoints.MovieStream(source, "1", ".mp4");

        // Assert
        url.AbsoluteUri.ShouldEndWith("/1.mp4");
    }

    [Fact]
    public void MovieStream_WithoutAContainer_IsRejected()
    {
        // Arrange: choosing one here would hide the decision from the resolver that has to make it.
        var source = new XtreamSourceBuilder().Build();

        // Act
        var act = () => XtreamEndpoints.MovieStream(source, "1", " ");

        // Assert
        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void MovieStream_WhenTheBaseUrlCarriesAPathPrefix_PreservesIt()
    {
        // Arrange
        var source = new XtreamSourceBuilder().WithBaseUrl("http://host/iptv-panel").Build();

        // Act
        var url = XtreamEndpoints.MovieStream(source, "1", "mp4");

        // Assert
        url.AbsolutePath.ShouldStartWith("/iptv-panel/movie/");
    }

    [Fact]
    public void Xmltv_TargetsTheGuideEndpoint()
    {
        // Arrange
        var source = new XtreamSourceBuilder().WithCredentials("alice", "secret").Build();

        // Act
        var url = XtreamEndpoints.Xmltv(source);

        // Assert
        url.AbsoluteUri.ShouldBe("http://panel.example:8080/xmltv.php?username=alice&password=secret");
    }

    [Fact]
    public void Playlist_RequestsTheExtendedM3uFormat()
    {
        // Arrange
        var source = new XtreamSourceBuilder().WithCredentials("alice", "secret").Build();

        // Act
        var url = XtreamEndpoints.Playlist(source, StreamFormat.MpegTs);

        // Assert
        url.AbsoluteUri.ShouldBe(
            "http://panel.example:8080/get.php?username=alice&password=secret&type=m3u_plus&output=ts");
    }
}
