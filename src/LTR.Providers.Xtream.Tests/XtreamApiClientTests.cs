using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace LTR.Providers.Xtream;

/// <summary>
/// Exercises the HTTP layer against a real server, focusing on the malformed and hostile responses
/// panels return in practice.
/// </summary>
public sealed class XtreamApiClientTests
{
    private const string ActiveAccountJson = """
        {
          "user_info": {
            "username": "alice",
            "auth": 1,
            "status": "Active",
            "exp_date": "1786000000",
            "is_trial": "0",
            "active_cons": "0",
            "max_connections": "2",
            "allowed_output_formats": ["ts", "m3u8"]
          },
          "server_info": { "url": "panel.example", "port": "8080" }
        }
        """;

    [Fact]
    public async Task AuthenticateAsync_ReadsAnAllStringUserInfoBlock()
    {
        // Arrange
        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            [string.Empty] = ActiveAccountJson,
        });

        var (client, source) = CreateClient(panel);

        // Act
        var response = await client.AuthenticateAsync(source, TestContext.Current.CancellationToken);

        // Assert
        response.UserInfo.ShouldNotBeNull();
        response.UserInfo.Auth.ShouldBe(1);
        response.UserInfo.MaxConnections.ShouldBe(2);
        response.UserInfo.AllowedOutputFormats.ShouldBe(["ts", "m3u8"]);
    }

    [Fact]
    public async Task AuthenticateAsync_WhenThePanelAnswersWithAnArray_ReportsNoUserInfo()
    {
        // Arrange: an empty array is how several panels signal rejected credentials.
        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            [string.Empty] = "[]",
        });

        var (client, source) = CreateClient(panel);

        // Act
        var response = await client.AuthenticateAsync(source, TestContext.Current.CancellationToken);

        // Assert
        response.UserInfo.ShouldBeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_WhenThePanelReturnsHtml_FailsWithAnActionableMessage()
    {
        // Arrange: a wrong base address or a blocking panel yields an HTML page with status 200.
        await using var panel = await FakePanel.StartAsync(async context =>
        {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("<!DOCTYPE html><html><body>Under maintenance</body></html>");
        });

        var (client, source) = CreateClient(panel);

        // Act
        var act = async () => await client.AuthenticateAsync(source, TestContext.Current.CancellationToken);

        // Assert
        var exception = await act.ShouldThrowAsync<XtreamApiException>();
        exception.Message.ShouldContain("HTML");
    }

    [Fact]
    public async Task AuthenticateAsync_WhenThePanelFails_ReportsTheStatusCodeWithoutLeakingCredentials()
    {
        // Arrange
        await using var panel = await FakePanel.StartAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Task.CompletedTask;
        });

        var (client, source) = CreateClient(panel);

        // Act
        var act = async () => await client.AuthenticateAsync(source, TestContext.Current.CancellationToken);

        // Assert
        var exception = await act.ShouldThrowAsync<XtreamApiException>();
        exception.Message.ShouldContain("503");
        exception.SanitizedUrl.ShouldNotBeNull();
        exception.SanitizedUrl.ShouldNotContain("s3cret");
        exception.SanitizedUrl.ShouldContain("***");
    }

    [Fact]
    public async Task AuthenticateAsync_WhenThePanelReturnsAnEmptyBody_Fails()
    {
        // Arrange: overloaded panels close the connection after sending headers only.
        await using var panel = await FakePanel.StartAsync(context => Task.CompletedTask);
        var (client, source) = CreateClient(panel);

        // Act
        var act = async () => await client.AuthenticateAsync(source, TestContext.Current.CancellationToken);

        // Assert
        await act.ShouldThrowAsync<XtreamApiException>();
    }

    [Fact]
    public async Task GetLiveStreamsAsync_ParsesTheChannelArray()
    {
        // Arrange
        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            ["get_live_streams"] = """
                [
                  { "num": 1, "name": "Erste", "stream_id": "101", "category_id": "5" },
                  { "num": "2", "name": "Zweite", "stream_id": 102, "tv_archive": "1" }
                ]
                """,
        });

        var (client, source) = CreateClient(panel);

        // Act
        var streams = await client.GetLiveStreamsAsync(source, TestContext.Current.CancellationToken);

        // Assert
        streams.Count.ShouldBe(2);
        streams[0].StreamId.ShouldBe("101");
        streams[1].StreamId.ShouldBe("102");
        streams[1].HasArchive.ShouldBeTrue();
    }

    [Fact]
    public async Task GetLiveStreamsAsync_WhenThePanelAnswersWithAnObject_YieldsAnEmptyList()
    {
        // Arrange: a panel that does not know the action falls back to the authentication object.
        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            ["get_live_streams"] = ActiveAccountJson,
        });

        var (client, source) = CreateClient(panel);

        // Act
        var streams = await client.GetLiveStreamsAsync(source, TestContext.Current.CancellationToken);

        // Assert
        streams.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetStringAsync_SendsTheConfiguredUserAgent()
    {
        // Arrange: panels filter on the agent, so the configured value must reach the wire.
        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            [string.Empty] = ActiveAccountJson,
        });

        var (client, source) = CreateClient(panel, userAgent: "VLC/3.0.21 LibVLC/3.0.21");

        // Act
        await client.AuthenticateAsync(source, TestContext.Current.CancellationToken);

        // Assert
        panel.ObservedUserAgents.ShouldContain("VLC/3.0.21 LibVLC/3.0.21");
    }

    [Fact]
    public async Task ProbeActionAsync_ReportsTheShapeThePanelAnswersWith()
    {
        // Arrange
        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            ["get_live_categories"] = "[]",
            ["get_short_epg"] = """{ "epg_listings": [] }""",
        });

        var (client, source) = CreateClient(panel);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var listShape = await client.ProbeActionAsync(source, "get_live_categories", null, cancellationToken);
        var epgShape = await client.ProbeActionAsync(source, "get_short_epg", null, cancellationToken);
        var missingShape = await client.ProbeActionAsync(source, "get_series", null, cancellationToken);

        // Assert
        listShape.ShouldBe(JsonValueKind.Array);
        epgShape.ShouldBe(JsonValueKind.Object);
        missingShape.ShouldBeNull("a 404 means the action is unavailable");
    }

    [Fact]
    public async Task ResourceExistsAsync_DistinguishesServedFromMissingResources()
    {
        // Arrange
        await using var panel = await FakePanel.StartAsync(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/xmltv.php"))
            {
                await context.Response.WriteAsync("<tv></tv>");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
        });

        var (client, source) = CreateClient(panel);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var guideExists = await client.ResourceExistsAsync(
            source,
            XtreamEndpoints.Xmltv(source),
            cancellationToken);

        var missingExists = await client.ResourceExistsAsync(
            source,
            new Uri(panel.BaseUrl, "does-not-exist.php"),
            cancellationToken);

        // Assert
        guideExists.ShouldBeTrue();
        missingExists.ShouldBeFalse();
    }

    [Fact]
    public async Task GetLiveStreamsAsync_FollowsARedirectToALoadBalancer()
    {
        // Arrange: panels routinely bounce clients to a streaming node with a 302.
        await using var panel = await FakePanel.StartAsync(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/player_api.php"))
            {
                context.Response.Redirect("/relocated.php");
                return;
            }

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("""[{ "name": "Erste", "stream_id": "101" }]""");
        });

        var (client, source) = CreateClient(panel);

        // Act
        var streams = await client.GetLiveStreamsAsync(source, TestContext.Current.CancellationToken);

        // Assert
        streams.Count.ShouldBe(1);
        streams[0].StreamId.ShouldBe("101");
    }

    private static (XtreamApiClient Client, Core.Sources.XtreamSource Source) CreateClient(
        FakePanel panel,
        string userAgent = "TestAgent/1.0")
    {
        var client = new XtreamApiClient(new HttpClient(), NullLogger<XtreamApiClient>.Instance);

        var source = new XtreamSourceBuilder()
            .WithBaseUrl(panel.BaseUrl.AbsoluteUri)
            .WithCredentials("alice", "s3cret")
            .WithUserAgent(userAgent)
            .Build();

        return (client, source);
    }
}
