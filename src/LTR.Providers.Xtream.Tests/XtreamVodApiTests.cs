using Microsoft.Extensions.Logging.Abstractions;

namespace LTR.Providers.Xtream;

/// <summary>
/// Exercises the film and series calls over HTTP, where the shapes that matter are the ones a panel
/// returns when it has nothing to give.
/// </summary>
public sealed class XtreamVodApiTests
{
    [Fact]
    public async Task GetVodStreamsAsync_ReadsTheListing()
    {
        // Arrange
        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            ["get_vod_streams"] = """
                [{ "name": "Arrival", "stream_id": 8412, "container_extension": "mkv" }]
                """,
        });

        var (client, source) = CreateClient(panel);

        // Act
        var streams = await client.GetVodStreamsAsync(source, TestContext.Current.CancellationToken);

        // Assert
        streams.ShouldHaveSingleItem().StreamId.ShouldBe("8412");
    }

    [Fact]
    public async Task GetSeriesAsync_WhenThePanelDoesNotKnowTheAction_ReportsAnEmptySection()
    {
        // Arrange: a panel without series answers the authentication object rather than an array.
        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            ["get_series"] = """{ "user_info": { "auth": 1 } }""",
        });

        var (client, source) = CreateClient(panel);

        // Act
        var series = await client.GetSeriesAsync(source, TestContext.Current.CancellationToken);

        // Assert
        series.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetSeriesInfoAsync_ReadsTheSeasonsAndEpisodes()
    {
        // Arrange
        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            ["get_series_info"] = """
                {
                  "info": { "plot": "A chemistry teacher." },
                  "seasons": [],
                  "episodes": { "1": [{ "id": "1001", "episode_num": 1, "title": "Pilot" }] }
                }
                """,
        });

        var (client, source) = CreateClient(panel);

        // Act
        var response = await client.GetSeriesInfoAsync(source, "4321", TestContext.Current.CancellationToken);

        // Assert
        response.ShouldNotBeNull();
        var detail = XtreamVodMapper.MapSeriesDetail(response);
        detail.Plot.ShouldBe("A chemistry teacher.");
        detail.Seasons.ShouldHaveSingleItem().Episodes.ShouldHaveSingleItem().ExternalId.ShouldBe("1001");
    }

    [Fact]
    public async Task GetVodInfoAsync_WithEmptyPhpArraysForItsBlocks_StillReadsAsNoDetail()
    {
        // Arrange: an empty associative array and an empty list are the same value in PHP, so a panel
        // with nothing to say about a film sends "[]" where an object belongs. Read tolerantly rather
        // than thrown on, because the film still plays perfectly well without a synopsis.
        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            ["get_vod_info"] = """{ "info": [], "movie_data": [] }""",
        });

        var (client, source) = CreateClient(panel);

        // Act
        var response = await client.GetVodInfoAsync(source, "8412", TestContext.Current.CancellationToken);

        // Assert
        response.ShouldNotBeNull();
        response.Info.ShouldBeNull();
        response.MovieData.ShouldBeNull();
    }

    [Fact]
    public async Task GetVodInfoAsync_WhenThePanelAnswersWithAnArray_ReportsNoDetail()
    {
        // Arrange: the whole response as a bare array is how panels answer for an unknown id.
        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            ["get_vod_info"] = "[]",
        });

        var (client, source) = CreateClient(panel);

        // Act
        var response = await client.GetVodInfoAsync(source, "9999", TestContext.Current.CancellationToken);

        // Assert
        response.ShouldBeNull();
    }

    [Fact]
    public async Task GetVodInfoAsync_WhenTheResponseCannotBeMapped_ReportsNoDetailRatherThanFailing()
    {
        // Arrange: a shape the tolerant converters do not cover. Opening a film's page must not become
        // an error over a field nobody displays.
        await using var panel = await FakePanel.StartAsync(new Dictionary<string, string>
        {
            ["get_vod_info"] = """{ "info": { "plot": { "en": "nested" } } }""",
        });

        var (client, source) = CreateClient(panel);

        // Act
        var response = await client.GetVodInfoAsync(source, "8412", TestContext.Current.CancellationToken);

        // Assert
        response.ShouldBeNull();
    }

    private static (XtreamApiClient Client, Core.Sources.XtreamSource Source) CreateClient(FakePanel panel)
    {
        var client = new XtreamApiClient(
            new HttpClient(),
            new XtreamUrlSanitizer(),
            NullLogger<XtreamApiClient>.Instance);

        var source = new XtreamSourceBuilder()
            .WithBaseUrl(panel.BaseUrl.AbsoluteUri)
            .WithCredentials("alice", "s3cret")
            .Build();

        return (client, source);
    }
}
