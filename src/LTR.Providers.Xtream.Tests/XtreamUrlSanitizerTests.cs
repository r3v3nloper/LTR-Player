using LTR.Core.Sources;

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
        // Arrange: a stream address repeats them as path segments rather than as parameters, which is why
        // the rule replaces by value and not by parameter name.
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
