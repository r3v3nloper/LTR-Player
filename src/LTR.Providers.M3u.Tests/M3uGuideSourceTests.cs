using System.IO;
using System.Text;
using LTR.Core.Sources;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace LTR.Providers.M3u;

/// <summary>
/// Covers the guide download over HTTP, including what a refusal reports.
/// </summary>
public sealed class M3uGuideSourceTests
{
    [Fact]
    public async Task TryReadGuideAsync_WhenTheGuideIsServed_HandsTheDocumentToTheReader()
    {
        // Arrange
        const string guide = """<?xml version="1.0"?><tv></tv>""";

        await using var host = await FakePlaylistHost.StartAsync(async context =>
        {
            context.Response.ContentType = "application/xml";
            await context.Response.WriteAsync(guide);
        });

        var source = SourceWithGuide(host.Address("xmltv.php?username=alice&password=s3cret"));
        var received = new StringBuilder();

        // Act
        var found = await CreateGuideSource().TryReadGuideAsync(
            source,
            async (stream, token) => received.Append(await ReadAllAsync(stream, token)),
            TestContext.Current.CancellationToken);

        // Assert
        found.ShouldBeTrue();
        received.ToString().ShouldBe(guide);
    }

    [Fact]
    public async Task TryReadGuideAsync_WhenTheGuideAddressIsRefused_ReportsTheSanitisedAddress()
    {
        // Arrange: a playlist's guide is either the address the user configured or the one the playlist
        // declared, and which was tried is the whole diagnosis — so the failure has to carry it.
        await using var host = await FakePlaylistHost.StartAsync(StatusCodes.Status404NotFound);
        var source = SourceWithGuide(host.Address("xmltv.php?username=alice&password=s3cret"));

        // Act
        var read = async () => await CreateGuideSource().TryReadGuideAsync(
            source,
            (_, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await read.ShouldThrowAsync<ProviderRequestException>();
        exception.Message.ShouldContain("404");
        exception.SanitizedUrl.ShouldNotBeNull();
        exception.SanitizedUrl.ShouldContain("xmltv.php");
        exception.SanitizedUrl.ShouldNotContain("alice");
        exception.SanitizedUrl.ShouldNotContain("s3cret");
    }

    [Fact]
    public async Task TryReadGuideAsync_WhenTheGuideAddressIsRefused_DoesNotCallTheReader()
    {
        // Arrange: a 404 body is an error page, and passing it on would be reported as a corrupt guide.
        await using var host = await FakePlaylistHost.StartAsync(StatusCodes.Status404NotFound);
        var source = SourceWithGuide(host.Address("xmltv.php"));
        var readCalled = false;

        // Act
        var read = async () => await CreateGuideSource().TryReadGuideAsync(
            source,
            (_, _) =>
            {
                readCalled = true;
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        // Assert
        await read.ShouldThrowAsync<ProviderRequestException>();
        readCalled.ShouldBeFalse();
    }

    private static M3uGuideSource CreateGuideSource()
    {
        var loader = new M3uPlaylistLoader(new HttpClient(), new M3uPlaylistCache(TimeProvider.System));

        return new M3uGuideSource(
            new HttpClient(),
            loader,
            new M3uUrlSanitizer(),
            NullLogger<M3uGuideSource>.Instance);
    }

    /// <summary>
    /// A source whose guide address is configured, so resolving it does not fetch the playlist.
    /// </summary>
    private static M3uSource SourceWithGuide(Uri guideUrl)
    {
        return new M3uSource
        {
            Name = "Playlist",
            PlaylistUrl = new Uri("http://host/get.php?username=alice&password=s3cret"),
            EpgUrl = guideUrl,
        };
    }

    private static async Task<string> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
