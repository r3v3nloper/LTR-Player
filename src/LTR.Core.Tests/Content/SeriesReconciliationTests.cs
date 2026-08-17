namespace LTR.Core.Content;

/// <summary>
/// The season and episode reconciliation, on its own.
/// </summary>
/// <remarks>
/// These cases were reachable only through real SQLite while the algorithm lived in the database context,
/// which is why there were two of them. They are all shapes a panel produces: an episode refiled into another
/// season, one listed under two seasons at once, and a season that has gone away. What each has to protect is
/// the same thing — the position the viewer reached in an episode is their own data, and matching by
/// identifier across the whole series is what carries it through.
/// </remarks>
public sealed class SeriesReconciliationTests
{
    [Fact]
    public void Apply_WhenAnEpisodeIsRefiledIntoAnotherSeason_KeepsTheViewersPosition()
    {
        // Arrange: the case the whole design is for. The same episode, moved from season 1 to season 2.
        var stored = SeriesWith(Season(1, Watched("e1", "Pilot", number: 1, atSeconds: 900)));

        var detail = new SeriesDetail([
            new Season { Number = 2, Episodes = [Fetched("e1", "Pilot", number: 1)] },
        ]);

        // Act
        var episodeCount = SeriesReconciliation.Apply(stored, detail);

        // Assert
        episodeCount.ShouldBe(1);

        var season = stored.Seasons.ShouldHaveSingleItem();
        season.Number.ShouldBe(2);

        var episode = season.Episodes.ShouldHaveSingleItem();
        episode.ExternalId.ShouldBe("e1");
        episode.ResumePositionSeconds.ShouldBe(900, "the viewer's place travels with the row");
    }

    [Fact]
    public void Apply_WhenTheProviderStopsListingAnEpisode_RemovesIt()
    {
        // Arrange
        var stored = SeriesWith(Season(
            1,
            Watched("e1", "Pilot", number: 1, atSeconds: 60),
            Watched("e2", "Second", number: 2, atSeconds: 60)));

        var detail = new SeriesDetail([
            new Season { Number = 1, Episodes = [Fetched("e1", "Pilot", number: 1)] },
        ]);

        // Act
        SeriesReconciliation.Apply(stored, detail);

        // Assert
        stored.Seasons.ShouldHaveSingleItem().Episodes.ShouldHaveSingleItem().ExternalId.ShouldBe("e1");
    }

    [Fact]
    public void Apply_WhenOneEpisodeIsListedUnderTwoSeasons_KeepsOneCopy()
    {
        // Arrange: a provider fault that does occur, and it must not fail an import or leave a duplicate.
        var stored = SeriesWith(Season(1, Watched("e1", "Pilot", number: 1, atSeconds: 30)));

        var detail = new SeriesDetail([
            new Season { Number = 1, Episodes = [Fetched("e1", "Pilot", number: 1)] },
            new Season { Number = 2, Episodes = [Fetched("e1", "Pilot", number: 1)] },
        ]);

        // Act
        var episodeCount = SeriesReconciliation.Apply(stored, detail);

        // Assert
        episodeCount.ShouldBe(2, "the count reports what the panel listed");

        stored.Seasons
            .SelectMany(season => season.Episodes)
            .Count(episode => episode.ExternalId == "e1")
            .ShouldBe(1, "however often it was listed, one row is stored");
    }

    [Fact]
    public void Apply_WhenASeasonIsGone_RemovesIt()
    {
        // Arrange
        var stored = SeriesWith(
            Season(1, Watched("e1", "Pilot", number: 1, atSeconds: 30)),
            Season(2, Watched("e2", "Later", number: 1, atSeconds: 30)));

        var detail = new SeriesDetail([
            new Season { Number = 2, Episodes = [Fetched("e2", "Later", number: 1)] },
        ]);

        // Act
        SeriesReconciliation.Apply(stored, detail);

        // Assert
        stored.Seasons.ShouldHaveSingleItem().Number.ShouldBe(2);
    }

    [Fact]
    public void Apply_WhereTheDetailIsSilent_LeavesWhatWasStored()
    {
        // Arrange: panels omit fields they have nothing to say about, and a fetch that assigned them
        // unconditionally would erase a synopsis one viewing at a time.
        var episode = Watched("e1", "Pilot", number: 1, atSeconds: 30);
        episode.Plot = "A chemistry teacher.";
        episode.ContainerExtension = "mkv";

        var stored = SeriesWith(Season(1, episode));

        var detail = new SeriesDetail([
            new Season
            {
                Number = 1,
                Episodes = [new Episode { ExternalId = "e1", Title = "Pilot (remastered)", Number = 1 }],
            },
        ]);

        // Act
        SeriesReconciliation.Apply(stored, detail);

        // Assert
        var reconciled = stored.Seasons.ShouldHaveSingleItem().Episodes.ShouldHaveSingleItem();
        reconciled.Title.ShouldBe("Pilot (remastered)", "the title is the provider's to state");
        reconciled.Plot.ShouldBe("A chemistry teacher.");
        reconciled.ContainerExtension.ShouldBe("mkv", "and the address is built from it");
    }

    [Fact]
    public void Apply_ForASeriesWithNothingStored_TakesTheWholeDetail()
    {
        // Arrange
        var stored = SeriesWith();

        var detail = new SeriesDetail([
            new Season { Number = 1, Episodes = [Fetched("e1", "Pilot", 1), Fetched("e2", "Second", 2)] },
        ]);

        // Act
        var episodeCount = SeriesReconciliation.Apply(stored, detail);

        // Assert
        episodeCount.ShouldBe(2);
        stored.Seasons.ShouldHaveSingleItem().Episodes.Count.ShouldBe(2);
    }

    private static Series SeriesWith(params Season[] seasons)
    {
        return new Series
        {
            Id = 1,
            SourceId = 1,
            ExternalId = "4321",
            Name = "Breaking Bad",
            Seasons = [.. seasons],
        };
    }

    private static Season Season(int number, params Episode[] episodes)
    {
        return new Season { Id = number, Number = number, Episodes = [.. episodes] };
    }

    /// <summary>An episode as stored, part-watched, so a lost position shows up as one.</summary>
    private static Episode Watched(string externalId, string title, int number, int atSeconds)
    {
        return new Episode
        {
            ExternalId = externalId,
            Title = title,
            Number = number,
            ResumePositionSeconds = atSeconds,
        };
    }

    private static Episode Fetched(string externalId, string title, int number)
    {
        return new Episode { ExternalId = externalId, Title = title, Number = number };
    }
}
