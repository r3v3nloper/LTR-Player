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

    /// <summary>
    /// A panel whose PHP file was saved with a byte-order mark emits one ahead of its response.
    /// </summary>
    /// <remarks>
    /// The parse survives one either way — <c>JsonDocument</c>'s stream overloads skip a mark themselves, and
    /// a mutation proved it. What does not survive is the inspection: a mark is not whitespace to .NET, so it
    /// sits in front of <c>&lt;html</c> and an error page reads as valid content the parser then chokes on,
    /// reported as malformed JSON. Both halves are here, and the second is the one that earns the skip.
    /// </remarks>
    [Fact]
    public async Task AuthenticateAsync_WhenThePanelPrefixesAByteOrderMark_StillReadsTheAccount()
    {
        // Arrange
        await using var panel = await FakePanel.StartAsync(context =>
            WriteWithByteOrderMarkAsync(context, "application/json", ActiveAccountJson));

        var (client, source) = CreateClient(panel);

        // Act
        var account = await client.AuthenticateAsync(source, TestContext.Current.CancellationToken);

        // Assert
        account.UserInfo.ShouldNotBeNull();
        account.UserInfo.Username.ShouldBe("alice");
    }

    [Fact]
    public async Task AuthenticateAsync_WhenAnHtmlPageCarriesAByteOrderMark_StillReportsItAsHtml()
    {
        // Arrange: the combination that reads as a parse failure rather than as what it is — a panel serving
        // its maintenance page, which is worth saying plainly because the address or the agent is the cause.
        await using var panel = await FakePanel.StartAsync(context => WriteWithByteOrderMarkAsync(
            context,
            "text/html",
            "<!DOCTYPE html><html><body>Under maintenance</body></html>"));

        var (client, source) = CreateClient(panel);

        // Act
        var act = async () => await client.AuthenticateAsync(source, TestContext.Current.CancellationToken);

        // Assert
        var exception = await act.ShouldThrowAsync<XtreamApiException>();
        exception.Message.ShouldContain("HTML");
    }

    /// <summary>
    /// A response longer than the peek window, which is the property the streamed read has to keep.
    /// </summary>
    /// <remarks>
    /// The emptiness and HTML checks look at the first bytes without consuming them. Get that wrong and a
    /// short response still parses — every other test here sends one — while a real channel list loses its
    /// opening bytes. Fifty entries is well past the window, and the assertions are on the first and last of
    /// them so that a loss at either end fails.
    /// </remarks>
    [Fact]
    public async Task GetLiveStreamsAsync_WhenTheResponseIsLongerThanThePeek_ParsesAllOfIt()
    {
        // Arrange
        var entries = Enumerable
            .Range(1, 50)
            .Select(number => $$"""{"stream_id": {{number}}, "name": "Channel {{number}}"}""");

        var json = $"[{string.Join(",", entries)}]";
        json.Length.ShouldBeGreaterThan(512, "the point of this test is a body past the peek window");

        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            ["get_live_streams"] = json,
        });

        var (client, source) = CreateClient(panel);

        // Act
        var streams = await client.GetLiveStreamsAsync(source, TestContext.Current.CancellationToken);

        // Assert
        streams.Count.ShouldBe(50);
        streams[0].Name.ShouldBe("Channel 1");
        streams[^1].Name.ShouldBe("Channel 50");
    }

    [Fact]
    public async Task GetLiveStreamsAsync_WhenThePanelReturnsMalformedJson_FailsWithAnActionableMessage()
    {
        // Arrange: truncated responses do arrive from panels that time out mid-write.
        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            ["get_live_streams"] = """[{"stream_id": 1, "name": "Erste"”""",
        });

        var (client, source) = CreateClient(panel);

        // Act
        var act = async () => await client.GetLiveStreamsAsync(source, TestContext.Current.CancellationToken);

        // Assert
        var exception = await act.ShouldThrowAsync<XtreamApiException>();
        exception.Message.ShouldContain("malformed");
        exception.SanitizedUrl.ShouldNotBeNull().ShouldNotContain("s3cret");
    }

    private static async Task WriteWithByteOrderMarkAsync(HttpContext context, string contentType, string body)
    {
        context.Response.ContentType = contentType;

        byte[] mark = [0xEF, 0xBB, 0xBF];
        await context.Response.Body.WriteAsync(mark);
        await context.Response.Body.WriteAsync(System.Text.Encoding.UTF8.GetBytes(body));
    }

    [Fact]
    public async Task AuthenticateAsync_SendsTheConfiguredUserAgent()
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
        var client = new XtreamApiClient(
            new HttpClient(),
            new XtreamUrlSanitizer(),
            NullLogger<XtreamApiClient>.Instance);

        var source = new XtreamSourceBuilder()
            .WithBaseUrl(panel.BaseUrl.AbsoluteUri)
            .WithCredentials("alice", "s3cret")
            .WithUserAgent(userAgent)
            .Build();

        return (client, source);
    }
}
