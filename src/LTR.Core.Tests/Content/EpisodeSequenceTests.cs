namespace LTR.Core.Content;

/// <summary>
/// Covers the order a series is watched in, and what "the next episode" means at each edge of it.
/// </summary>
/// <remarks>
/// The two cases that carry the bug this was written for are the season boundary and the end of the series.
/// Both have a wrong version that passes every within-a-season test: one stops at the end of each season, and
/// the other wraps round and starts the series again.
/// </remarks>
public sealed class EpisodeSequenceTests
{
    [Fact]
    public void Neighbour_WithinASeason_IsTheFollowingEpisode()
    {
        // Arrange
        var series = SeriesOf((1, [1, 2, 3]));

        // Act
        var next = EpisodeSequence.Neighbour(series, EpisodeId(1, 1), offset: 1);

        // Assert
        next.ShouldNotBeNull();
        next.SeasonNumber.ShouldBe(1);
        next.Episode.Number.ShouldBe(2);
    }

    [Fact]
    public void Neighbour_AtTheEndOfASeason_IsTheFirstEpisodeOfTheNext()
    {
        // Arrange
        var series = SeriesOf((1, [1, 2]), (2, [1, 2]));

        // Act
        var next = EpisodeSequence.Neighbour(series, EpisodeId(1, 2), offset: 1);

        // Assert
        next.ShouldNotBeNull();
        next.SeasonNumber.ShouldBe(2);
        next.Episode.Number.ShouldBe(1);
    }

    [Fact]
    public void Neighbour_AtTheStartOfASeason_IsTheLastEpisodeOfThePrevious()
    {
        // Arrange
        var series = SeriesOf((1, [1, 2, 3]), (2, [1]));

        // Act
        var previous = EpisodeSequence.Neighbour(series, EpisodeId(2, 1), offset: -1);

        // Assert
        previous.ShouldNotBeNull();
        previous.SeasonNumber.ShouldBe(1);
        previous.Episode.Number.ShouldBe(3);
    }

    /// <remarks>
    /// Nothing rather than the first episode. A series that restarted itself after its finale would look like
    /// the button having done something else entirely.
    /// </remarks>
    [Fact]
    public void Neighbour_PastTheLastEpisode_IsNothing()
    {
        // Arrange
        var series = SeriesOf((1, [1, 2]), (2, [1]));

        // Act
        var next = EpisodeSequence.Neighbour(series, EpisodeId(2, 1), offset: 1);

        // Assert
        next.ShouldBeNull();
    }

    [Fact]
    public void Neighbour_BeforeTheFirstEpisode_IsNothing()
    {
        // Arrange
        var series = SeriesOf((1, [1, 2]));

        // Act
        var previous = EpisodeSequence.Neighbour(series, EpisodeId(1, 1), offset: -1);

        // Assert
        previous.ShouldBeNull();
    }

    [Fact]
    public void Neighbour_ForAnEpisodeOfAnotherSeries_IsNothing()
    {
        // Arrange
        var series = SeriesOf((1, [1, 2]));

        // Act
        var next = EpisodeSequence.Neighbour(series, episodeId: 9999, offset: 1);

        // Assert
        next.ShouldBeNull();
    }

    /// <remarks>
    /// Panels list a season fetched later at the end rather than in place, and a season's episodes in whatever
    /// order the map happened to enumerate. Trusting either would make season two's opener the successor of
    /// season one's opener.
    /// </remarks>
    [Fact]
    public void InViewingOrder_WithTheProvidersOrderScrambled_IsBySeasonThenEpisode()
    {
        // Arrange
        var series = SeriesOf((2, [2, 1]), (1, [3, 1, 2]));

        // Act
        var ordered = EpisodeSequence.InViewingOrder(series);

        // Assert
        ordered
            .Select(entry => (entry.SeasonNumber, entry.Episode.Number))
            .ShouldBe([(1, 1), (1, 2), (1, 3), (2, 1), (2, 2)]);
    }

    /// <summary>
    /// Builds a series from season numbers and the episode numbers each holds, in the given order.
    /// </summary>
    private static Series SeriesOf(params (int Season, int[] Episodes)[] seasons)
    {
        return new Series
        {
            ExternalId = "1",
            Name = "Breaking Bad",
            Seasons =
            [
                .. seasons.Select(season => new Season
                {
                    Number = season.Season,
                    Episodes =
                    [
                        .. season.Episodes.Select(number => new Episode
                        {
                            Id = EpisodeId(season.Season, number),
                            ExternalId = EpisodeId(season.Season, number).ToString(CultureInfo.InvariantCulture),
                            Title = $"S{season.Season}E{number}",
                            Number = number,
                        }),
                    ],
                }),
            ],
        };
    }

    /// <summary>A stable identifier per season and episode number, so a test can name the one it means.</summary>
    private static int EpisodeId(int seasonNumber, int episodeNumber)
    {
        return (seasonNumber * 100) + episodeNumber;
    }
}
