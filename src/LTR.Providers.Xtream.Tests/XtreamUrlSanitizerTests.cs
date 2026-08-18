using LTR.Core.Sources;
using LTR.TestSupport;

namespace LTR.Providers.Xtream;

/// <summary>
/// Covers the Xtream rule and, through it, the shared parts of <see cref="SensitiveUrlSanitizer{T}"/>.
/// </summary>
/// <remarks>
/// Every assertion here is a variant of one question: could a logged address still be used to sign in?
/// The addresses are built through <see cref="XtreamEndpoints"/> rather than typed out, so a change to
/// how one is composed cannot leave the sanitiser passing against a shape nothing produces.
/// </remarks>
public sealed class XtreamUrlSanitizerTests
{
    private readonly XtreamUrlSanitizer _sanitizer = new();

    [Fact]
    public void Sanitize_RemovesTheCredentialsFromAnApiQueryString()
    {
        // Arrange
        var source = new XtreamSourceBuilder().WithCredentials("alice", "s3cret").Build();
        var url = XtreamEndpoints.PlayerApi(source, "get_live_streams");

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldNotContain("alice");
        sanitized.ShouldNotContain("s3cret");
    }

    [Fact]
    public void Sanitize_KeepsTheActionAndTheHost()
    {
        // Arrange: a redacted address still has to answer "which call, against which panel?", or logging
        // it achieves nothing.
        var source = new XtreamSourceBuilder()
            .WithBaseUrl("http://panel.example:8080")
            .WithCredentials("alice", "s3cret")
            .Build();

        var url = XtreamEndpoints.PlayerApi(source, "get_vod_info", [new("vod_id", "42")]);

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldContain("panel.example:8080");
        sanitized.ShouldContain("action=get_vod_info");
        sanitized.ShouldContain("vod_id=42");
    }

    [Fact]
    public void Sanitize_RemovesTheCredentialsFromAStreamPath()
    {
        // Arrange: a stream address spells them as whole path segments rather than as parameters, which is
        // why the rule has to look at the path as well as at the query.
        var source = new XtreamSourceBuilder().WithCredentials("alice", "s3cret").Build();

        var url = XtreamEndpoints.LiveStream(
            source,
            "1234",
            Core.Content.StreamFormat.MpegTs,
            useLivePathSegment: true);

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldNotContain("alice");
        sanitized.ShouldNotContain("s3cret");
        sanitized.ShouldContain("1234");
    }

    [Fact]
    public void Sanitize_RemovesCredentialsThatTheAddressPercentEncoded()
    {
        // Arrange: panels are handed whatever the subscription issued, and a password with a reserved
        // character reaches the query string escaped. Matching only the raw form would leak this one.
        const string username = "alice+1";
        const string password = "s3 cr@t/2";

        var source = new XtreamSourceBuilder().WithCredentials(username, password).Build();
        var url = XtreamEndpoints.PlayerApi(source, "get_series");

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert: the escaped forms, because those are what the address actually holds.
        sanitized.ShouldNotContain(Uri.EscapeDataString(username));
        sanitized.ShouldNotContain(Uri.EscapeDataString(password));
        sanitized.ShouldNotContain("cr%40t");
        sanitized.ShouldContain("action=get_series");
    }

    [Fact]
    public void Sanitize_WithAOneCharacterCredential_LeavesTheHostAndActionReadable()
    {
        // Arrange: the defect this rule replaced. Replacing "x" and "y" by value turned hd-max.org into
        // hd-ma***.org and player_api.php into pla***er_api.php — the two things a logged address is for.
        var source = new XtreamSourceBuilder()
            .WithBaseUrl("http://hd-max.org:8080")
            .WithCredentials("x", "y")
            .Build();

        var url = XtreamEndpoints.PlayerApi(source, "get_live_streams");

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldBe(
            "http://hd-max.org:8080/player_api.php?username=***&password=***&action=get_live_streams");
    }

    [Fact]
    public void Sanitize_WithACredentialThatOccursInTheHost_KeepsTheHostIntact()
    {
        // Arrange: the same problem from the other end — a credential that happens to be a word in the
        // address. "max" is a plausible username and appears in this panel's own name.
        var source = new XtreamSourceBuilder()
            .WithBaseUrl("http://hd-max.org:8080")
            .WithCredentials("max", "s3cret")
            .Build();

        var url = XtreamEndpoints.PlayerApi(source, "get_series");

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldContain("hd-max.org:8080");
        sanitized.ShouldContain("username=***");
        sanitized.ShouldContain("action=get_series");
    }

    [Fact]
    public void Sanitize_WithACredentialInAShapeThisClientNeverBuilds_StillRemovesIt()
    {
        // Arrange: the fallback, and the reason precision is safe here. A panel is free to answer with an
        // address of its own devising, and a credential must not survive into a log because the shape was
        // unfamiliar — so an address that spells it in no recognisable place is redacted wholesale, as every
        // address used to be.
        var source = new XtreamSourceBuilder().WithCredentials("alice", "s3cret").Build();
        var url = new Uri("http://panel.example:8080/redirect?to=session-alice-s3cret-42");

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldNotContain("alice");
        sanitized.ShouldNotContain("s3cret");
    }

    [Fact]
    public void Sanitize_WithOneCredentialInPlaceAndTheOtherNot_FallsBackForThatOneAlone()
    {
        // Arrange: judged per credential, so a properly spelled username does not exempt a password hidden
        // somewhere else in the same address.
        var source = new XtreamSourceBuilder().WithCredentials("alice", "s3cret").Build();
        var url = new Uri("http://panel.example:8080/player_api.php?username=alice&token=s3cret-42");

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldNotContain("alice");
        sanitized.ShouldNotContain("s3cret");
        sanitized.ShouldContain("username=***");
    }

    [Fact]
    public void Sanitize_RemovesTheUserInfoComponent()
    {
        // Arrange: the base class's own rule. Any scheme may carry user:password@host, and the values
        // there need not be the ones configured on the source.
        var source = new XtreamSourceBuilder().WithCredentials("alice", "s3cret").Build();
        var url = new Uri("http://someone:hunter2@panel.example:8080/player_api.php");

        // Act
        var sanitized = _sanitizer.Sanitize(url, source);

        // Assert
        sanitized.ShouldNotContain("someone");
        sanitized.ShouldNotContain("hunter2");
        sanitized.ShouldContain("panel.example:8080");
    }

    [Fact]
    public void Sanitize_ForASourceOfAnotherProtocol_Refuses()
    {
        // Arrange: the registry picks by protocol, so reaching here with the wrong source means a
        // registration is wrong — and silently returning the address unredacted would hide it.
        var source = new M3uSource { Name = "Playlist", PlaylistUrl = new Uri("http://host/get.php") };

        // Act
        var sanitize = () => _sanitizer.Sanitize(new Uri("http://host/x"), source);

        // Assert
        sanitize.ShouldThrow<NotSupportedException>().Message.ShouldContain(nameof(M3uSource));
    }

    [Fact]
    public void Supports_AcceptsXtreamSourcesOnly()
    {
        // Arrange
        var xtream = new XtreamSourceBuilder().Build();
        var playlist = new M3uSource { Name = "Playlist", PlaylistUrl = new Uri("http://host/get.php") };

        // Act & Assert
        _sanitizer.Supports(xtream).ShouldBeTrue();
        _sanitizer.Supports(playlist).ShouldBeFalse();
    }
}
