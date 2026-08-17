using LTR.Core.Sources;

namespace LTR.Providers.M3u;

/// <summary>
/// Covers the playlist rule: with no credentials to compare against, every query value is treated as one.
/// </summary>
public sealed class M3uUrlSanitizerTests
{
    private readonly M3uUrlSanitizer _sanitizer = new();

    [Fact]
    public void Sanitize_RemovesEveryQueryValueAndKeepsTheNames()
    {
        // Arrange: the shape a subscription playlist link actually takes.
        var url = new Uri("http://host:8080/get.php?username=alice&password=s3cret&type=m3u_plus");
        var source = SourceFor(url);

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldBe("http://host:8080/get.php?username=***&password=***&type=***");
    }

    [Fact]
    public void Sanitize_RedactsAValueItDoesNotRecogniseAsSecret()
    {
        // Arrange: the point of the structural rule. Nothing here knows what a given panel calls its
        // credentials, so a parameter named anything at all still has its value removed.
        var url = new Uri("http://host/playlist?t=9f8e7d6c&u=alice");
        var source = SourceFor(url);

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldNotContain("9f8e7d6c");
        sanitized.ShouldNotContain("alice");
        sanitized.ShouldContain("t=");
        sanitized.ShouldContain("u=");
    }

    [Fact]
    public void Sanitize_RemovesAParameterThatHasNoNameAtAll()
    {
        // Arrange: a bare query string is as likely to be a token as a flag, and there is no name worth
        // keeping either way.
        var url = new Uri("http://host/playlist?9f8e7d6c");
        var source = SourceFor(url);

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldBe("http://host/playlist?***");
    }

    [Fact]
    public void Sanitize_WithoutAQueryString_LeavesTheAddressAlone()
    {
        // Arrange: nothing to redact, and the address is the whole diagnostic value.
        var url = new Uri("http://host:8080/subscription/list.m3u");
        var source = SourceFor(url);

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldBe("http://host:8080/subscription/list.m3u");
    }

    [Fact]
    public void Sanitize_RemovesTheUserInfoComponent()
    {
        // Arrange: the other form a playlist link carries credentials in, handled by the base class.
        var url = new Uri("http://alice:s3cret@host/playlist.m3u");
        var source = SourceFor(url);

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldNotContain("alice");
        sanitized.ShouldNotContain("s3cret");
        sanitized.ShouldContain("host");
        sanitized.ShouldContain("playlist.m3u");
    }

    [Fact]
    public void Sanitize_SanitizesTheGuideAddressAsWellAsThePlaylist()
    {
        // Arrange: a separate XMLTV address carries the same credentials, and is the second address the
        // backlog item named. One source, two addresses, one rule.
        var source = new M3uSource
        {
            Name = "Playlist",
            PlaylistUrl = new Uri("http://host/get.php?username=alice&password=s3cret"),
            EpgUrl = new Uri("http://host/xmltv.php?username=alice&password=s3cret"),
        };

        // Act
        var sanitized = _sanitizer.Sanitize(source.EpgUrl, source);

        // Assert
        sanitized.ShouldBe("http://host/xmltv.php?username=***&password=***");
    }

    [Fact]
    public void Sanitize_RemovesPathCredentialsTheSourcesOwnAddressReveals()
    {
        // Arrange: the leak this rule was written for. A channel address spells the credentials as path
        // segments, and the source's own playlist address is where their values are on record.
        var source = new M3uSource
        {
            Name = "Playlist",
            PlaylistUrl = new Uri("http://host/get.php?username=alice&password=s3cret&type=m3u_plus"),
        };

        var channel = new Uri("http://host/live/alice/s3cret/101.ts");

        // Act
        var sanitized = _sanitizer.Sanitize(channel, source);

        // Assert
        sanitized.ShouldBe("http://host/live/***/***/101.ts");
    }

    [Fact]
    public void Sanitize_WithKnownCredentials_KeepsTheRouteAndTheChannel()
    {
        // Arrange: the reason the match is per whole segment. "ts" is a query value of the playlist address,
        // and replacing it wherever it occurred would take the extension off every channel — the mistake the
        // Xtream sanitiser was corrected for.
        var source = new M3uSource
        {
            Name = "Playlist",
            PlaylistUrl = new Uri("http://host/get.php?username=alice&password=s3cret&output=ts"),
        };

        // Act
        var sanitized = _sanitizer.Sanitize(new Uri("http://host/live/alice/s3cret/101.ts"), source);

        // Assert
        // A route segment is not a credential, and neither is the channel.
        sanitized.ShouldContain("/live/");
        sanitized.ShouldContain("101.ts");
    }

    [Fact]
    public void Sanitize_TakesCredentialsFromTheGuideAddressToo()
    {
        // Arrange: a subscription's guide link carries the same credentials, and either may be the one the
        // user pasted — this source was added by its playlist path with the guide declared separately.
        var source = new M3uSource
        {
            Name = "Playlist",
            PlaylistUrl = new Uri("http://host/subscription.m3u"),
            EpgUrl = new Uri("http://host/xmltv.php?username=alice&password=s3cret"),
        };

        // Act
        var sanitized = _sanitizer.Sanitize(new Uri("http://host/live/alice/s3cret/101.ts"), source);

        // Assert
        sanitized.ShouldNotContain("alice");
        sanitized.ShouldNotContain("s3cret");
    }

    [Fact]
    public void Sanitize_WhenNothingRevealsTheCredentials_LeavesThePathAsItStands()
    {
        // Arrange: the limit that remains, and it is deliberate. A playlist held as a local file has no query
        // anywhere, so nothing distinguishes a secret segment from a route — and the CLI says as much rather
        // than claiming a masking it did not perform.
        var source = SourceFor(new Uri(@"C:\playlists\subscription.m3u"));

        // Act
        var sanitized = _sanitizer.Sanitize(new Uri("http://host/live/alice/s3cret/101.ts"), source);

        // Assert
        sanitized.ShouldBe("http://host/live/alice/s3cret/101.ts");
    }

    [Fact]
    public void Sanitize_ForALocalFile_LeavesThePathReadable()
    {
        // Arrange: a playlist the user was sent as a file has no credentials, and the path is what makes
        // a failure to read it diagnosable.
        var url = new Uri(@"C:\playlists\subscription.m3u");
        var source = SourceFor(url);

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldContain("subscription.m3u");
    }

    [Fact]
    public void Sanitize_ForARelativeAddress_ReturnsItRatherThanThrowing()
    {
        // Arrange: a sanitiser runs on the path that reports a failure, so it must not become the
        // failure. A relative address has no absolute form to ask for, which is where that would happen.
        var url = new Uri("playlist.m3u?token=9f8e7d6c", UriKind.Relative);
        var source = SourceFor(new Uri("http://host/get.php"));

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldNotContain("9f8e7d6c");
    }

    [Fact]
    public void Supports_AcceptsM3uSourcesOnly()
    {
        // Arrange
        var playlist = SourceFor(new Uri("http://host/get.php"));

        var panel = new XtreamSource
        {
            Name = "Panel",
            BaseUrl = new Uri("http://panel.example"),
            Username = "alice",
            Password = "s3cret",
        };

        // Act & Assert
        _sanitizer.Supports(playlist).ShouldBeTrue();
        _sanitizer.Supports(panel).ShouldBeFalse();
    }

    private static M3uSource SourceFor(Uri playlistUrl)
    {
        return new M3uSource { Name = "Playlist", PlaylistUrl = playlistUrl };
    }
}
