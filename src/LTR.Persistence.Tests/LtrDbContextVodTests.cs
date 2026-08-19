using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using Microsoft.EntityFrameworkCore;
using static LTR.Persistence.VodFixtures;

namespace LTR.Persistence;

/// <summary>
/// The film and series half of the store, against real SQLite.
/// </summary>
/// <remarks>
/// What matters here is what survives a refresh. The provider owns nearly every field, the viewer owns
/// the position they reached, and a detail call owns the fields a listing leaves blank — so almost every
/// test below is about something that must *not* be overwritten.
/// </remarks>
public sealed class LtrDbContextVodTests
{
    [Fact]
    public async Task ReconcileVodCatalogueAsync_StoresFilmsAndSeriesWithTheirCategories()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        // Act
        await using (var context = database.CreateContext())
        {
            await context.ReconcileVodCatalogueAsync(
                sourceId,
                [Category("58", "Action", ContentKind.Movie), Category("75", "Drama", ContentKind.Series)],
                [Movie("1", "Arrival", categoryExternalId: "58")],
                [SeriesEntry("4321", "Breaking Bad", categoryExternalId: "75")],
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();

        var movie = (await verifyContext.GetMoviesAsync(sourceId, cancellationToken)).ShouldHaveSingleItem();
        movie.Name.ShouldBe("Arrival");
        movie.CategoryId.ShouldNotBeNull();

        var series = (await verifyContext.GetSeriesAsync(sourceId, cancellationToken)).ShouldHaveSingleItem();
        series.Name.ShouldBe("Breaking Bad");
        series.CategoryId.ShouldNotBeNull();
        series.CategoryId.ShouldNotBe(movie.CategoryId, "the two kinds are different categories");
    }

    /// <summary>
    /// A panel numbers its category identifiers per section, so "58" is both a live category and a film
    /// category. Resolving by identifier alone would map a film to the live category of the same number.
    /// </summary>
    [Fact]
    public async Task ReconcileVodCatalogueAsync_WhenALiveCategoryHasTheSameIdentifier_KeepsThemApart()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                sourceId,
                [Category("58", "Sport", ContentKind.Live)],
                [Channel("101", "Erste", categoryExternalId: "58")],
                SixPm,
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.ReconcileVodCatalogueAsync(
                sourceId,
                [Category("58", "Action", ContentKind.Movie)],
                [Movie("1", "Arrival", categoryExternalId: "58")],
                [],
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();

        var channel = (await verifyContext.GetLiveChannelsAsync(sourceId, cancellationToken))
            .ShouldHaveSingleItem();
        var movie = (await verifyContext.GetMoviesAsync(sourceId, cancellationToken)).ShouldHaveSingleItem();

        channel.CategoryId.ShouldNotBeNull();
        movie.CategoryId.ShouldNotBeNull();
        movie.CategoryId.ShouldNotBe(channel.CategoryId);
    }

    /// <summary>
    /// The live import knows nothing about films. A reconciliation that removed every category it was not
    /// told about would have a live refresh delete the whole film category tree.
    /// </summary>
    [Fact]
    public async Task ReconcileLiveCatalogueAsync_LeavesTheFilmCategoriesAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.ReconcileVodCatalogueAsync(
                sourceId,
                [Category("58", "Action", ContentKind.Movie)],
                [Movie("1", "Arrival", categoryExternalId: "58")],
                [],
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                sourceId,
                [Category("10", "Sport", ContentKind.Live)],
                [Channel("101", "Erste", categoryExternalId: "10")],
                SixPm,
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();

        var filmCategories = await verifyContext.GetCategoriesAsync(
            sourceId,
            ContentKind.Movie,
            cancellationToken);

        filmCategories.ShouldHaveSingleItem().Name.ShouldBe("Action");
        (await verifyContext.GetMoviesAsync(sourceId, cancellationToken))
            .ShouldHaveSingleItem()
            .CategoryId.ShouldNotBeNull("the film is still categorised");
    }

    [Fact]
    public async Task ReconcileVodCatalogueAsync_KeepsTheStoredPositionOfAFilmThatIsStillOffered()
    {
        // Arrange: where the viewer left off is their own data, exactly like a favourite channel.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var movieId = await StoreOneMovieAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.RecordMovieProgressAsync(
                movieId,
                WatchOutcome.Resumable,
                TimeSpan.FromMinutes(40),
                SixPm,
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.ReconcileVodCatalogueAsync(
                sourceId,
                [],
                [Movie("1", "Arrival (renamed)")],
                [],
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var movie = (await verifyContext.GetMoviesAsync(sourceId, cancellationToken)).ShouldHaveSingleItem();
        movie.Name.ShouldBe("Arrival (renamed)", "the provider owns the name");
        movie.ResumePositionSeconds.ShouldBe(2400, "the viewer owns the position");
    }

    [Fact]
    public async Task ReconcileVodCatalogueAsync_DoesNotBlankOutWhatADetailCallSupplied()
    {
        // Arrange: panels state a synopsis in the detail response and not in the listing, so assigning the
        // listing's fields unconditionally would erase every synopsis the player had fetched.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var movieId = await StoreOneMovieAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.SaveMovieDetailAsync(
                movieId,
                new MovieDetail(
                    Plot: "Linguist meets heptapods.",
                    Genre: "Science Fiction",
                    Year: 2016,
                    DurationSeconds: 6_960,
                    ContainerExtension: "mkv"),
                SixPm,
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.ReconcileVodCatalogueAsync(sourceId, [], [Movie("1", "Arrival")], [], cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var movie = (await verifyContext.GetMoviesAsync(sourceId, cancellationToken)).ShouldHaveSingleItem();
        movie.Plot.ShouldBe("Linguist meets heptapods.");
        movie.Year.ShouldBe(2016);
        movie.ContainerExtension.ShouldBe("mkv");
        movie.DurationSeconds.ShouldBe(6_960);
        movie.HasDetail.ShouldBeTrue("otherwise the detail call is made again on every viewing");
    }

    [Fact]
    public async Task ReconcileVodCatalogueAsync_RemovesWhatTheProviderNoLongerOffers()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.ReconcileVodCatalogueAsync(
                sourceId,
                [],
                [Movie("1", "Kept"), Movie("2", "Withdrawn")],
                [SeriesEntry("10", "Kept series"), SeriesEntry("11", "Withdrawn series")],
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.ReconcileVodCatalogueAsync(
                sourceId,
                [],
                [Movie("1", "Kept")],
                [SeriesEntry("10", "Kept series")],
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        (await verifyContext.GetMoviesAsync(sourceId, cancellationToken))
            .ShouldHaveSingleItem()
            .ExternalId.ShouldBe("1");
        (await verifyContext.GetSeriesAsync(sourceId, cancellationToken))
            .ShouldHaveSingleItem()
            .ExternalId.ShouldBe("10");
    }

    [Fact]
    public async Task ReconcileVodCatalogueAsync_AdoptsANewLastModifiedSoStaleSeasonsAreRefetched()
    {
        // Arrange: this is the one series field a refresh must always take, because it is what tells
        // stored seasons apart from stale ones.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var seriesId = await StoreOneSeriesAsync(database, sourceId, SixPm, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.SaveSeriesDetailAsync(
                seriesId,
                new SeriesDetail([SeasonWith(1, Episode("1001", "Pilot", 1))]),
                SixPm,
                SixPm,
                cancellationToken);
        }

        // Act: the provider reports the series changed.
        await using (var context = database.CreateContext())
        {
            var refreshed = SeriesEntry("4321", "Breaking Bad");
            refreshed.LastModifiedUtc = SixPm.AddDays(1);

            await context.ReconcileVodCatalogueAsync(sourceId, [], [], [refreshed], cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var series = (await verifyContext.GetSeriesAsync(sourceId, cancellationToken)).ShouldHaveSingleItem();
        series.LastModifiedUtc.ShouldBe(SixPm.AddDays(1));
        series.HasCurrentDetail.ShouldBeFalse("the stored seasons are now known to be stale");
    }

    [Fact]
    public async Task SaveSeriesDetailAsync_StoresSeasonsAndEpisodesInOrder()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var seriesId = await StoreOneSeriesAsync(database, sourceId, SixPm, cancellationToken);

        // Act
        int episodeCount;

        await using (var context = database.CreateContext())
        {
            episodeCount = await context.SaveSeriesDetailAsync(
                seriesId,
                new SeriesDetail(
                    [
                        SeasonWith(2, Episode("2001", "Seven Thirty-Seven", 1)),
                        SeasonWith(1, Episode("1002", "Cat's in the Bag...", 2), Episode("1001", "Pilot", 1)),
                    ],
                    Plot: "A chemistry teacher."),
                SixPm,
                SixPm,
                cancellationToken);
        }

        // Assert
        episodeCount.ShouldBe(3);

        await using var verifyContext = database.CreateContext();
        var series = await verifyContext.GetSeriesDetailAsync(seriesId, cancellationToken);
        series.ShouldNotBeNull();
        series.Plot.ShouldBe("A chemistry teacher.");
        series.HasCurrentDetail.ShouldBeTrue();

        var seasons = series.Seasons.ToList();
        seasons.Select(season => season.Number).ShouldBe([1, 2]);
        seasons[0].Episodes.Select(episode => episode.Number).ShouldBe([1, 2]);
    }

    [Fact]
    public async Task SaveSeriesDetailAsync_OnASecondFetch_KeepsTheEpisodePositionsAndAddsTheNewEpisodes()
    {
        // Arrange: a season gaining an episode is the ordinary reason a series is fetched twice, and the
        // viewer's place in the episodes they already watched has to survive it.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var seriesId = await StoreOneSeriesAsync(database, sourceId, SixPm, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.SaveSeriesDetailAsync(
                seriesId,
                new SeriesDetail([SeasonWith(1, Episode("1001", "Pilot", 1))]),
                SixPm,
                SixPm,
                cancellationToken);
        }

        var episodeId = await SingleEpisodeIdAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.RecordEpisodeProgressAsync(
                episodeId,
                WatchOutcome.Resumable,
                TimeSpan.FromMinutes(20),
                SixPm,
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.SaveSeriesDetailAsync(
                seriesId,
                new SeriesDetail(
                    [SeasonWith(1, Episode("1001", "Pilot", 1), Episode("1002", "Cat's in the Bag...", 2))]),
                SixPm.AddDays(1),
                SixPm.AddDays(1),
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var episodes = (await verifyContext.GetSeriesDetailAsync(seriesId, cancellationToken))!
            .Seasons.Single()
            .Episodes.ToList();

        episodes.Count.ShouldBe(2);
        episodes.Single(episode => episode.ExternalId == "1001").ResumePositionSeconds.ShouldBe(1200);
        episodes.Single(episode => episode.ExternalId == "1002").ResumePositionSeconds.ShouldBeNull();
    }

    [Fact]
    public async Task SaveSeriesDetailAsync_WhenAnEpisodeIsRefiledIntoAnotherSeason_MovesItWithItsPosition()
    {
        // Arrange: providers do correct their own season numbering, and matching episodes within a season
        // rather than across the series would treat the correction as a deletion and an insertion.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var seriesId = await StoreOneSeriesAsync(database, sourceId, SixPm, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.SaveSeriesDetailAsync(
                seriesId,
                new SeriesDetail([SeasonWith(1, Episode("1001", "Pilot", 1))]),
                SixPm,
                SixPm,
                cancellationToken);
        }

        var episodeId = await SingleEpisodeIdAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.RecordEpisodeProgressAsync(
                episodeId,
                WatchOutcome.Resumable,
                TimeSpan.FromMinutes(20),
                SixPm,
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.SaveSeriesDetailAsync(
                seriesId,
                new SeriesDetail([SeasonWith(2, Episode("1001", "Pilot", 1))]),
                SixPm.AddDays(1),
                SixPm.AddDays(1),
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var season = (await verifyContext.GetSeriesDetailAsync(seriesId, cancellationToken))!
            .Seasons.ShouldHaveSingleItem();

        season.Number.ShouldBe(2);
        var episode = season.Episodes.ShouldHaveSingleItem();
        episode.Id.ShouldBe(episodeId, "the same row moved rather than being replaced");
        episode.ResumePositionSeconds.ShouldBe(1200);
    }

    [Fact]
    public async Task SaveSeriesDetailAsync_RemovesEpisodesAndSeasonsTheProviderDropped()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var seriesId = await StoreOneSeriesAsync(database, sourceId, SixPm, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.SaveSeriesDetailAsync(
                seriesId,
                new SeriesDetail(
                    [
                        SeasonWith(1, Episode("1001", "Pilot", 1), Episode("1002", "Second", 2)),
                        SeasonWith(2, Episode("2001", "Later", 1)),
                    ]),
                SixPm,
                SixPm,
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.SaveSeriesDetailAsync(
                seriesId,
                new SeriesDetail([SeasonWith(1, Episode("1001", "Pilot", 1))]),
                SixPm.AddDays(1),
                SixPm.AddDays(1),
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        (await verifyContext.Seasons.CountAsync(cancellationToken)).ShouldBe(1);
        (await verifyContext.Episodes.CountAsync(cancellationToken)).ShouldBe(1);
    }

    /// <summary>
    /// Starting at an episode reaches the whole series, which is what "the next episode" is answered from.
    /// </summary>
    /// <remarks>
    /// Against real SQLite because the lookup crosses two joins the player never states — episode to season to
    /// series — and then leans on the ordering <see cref="LtrDbContext.GetSeriesDetailAsync"/> applies. An
    /// in-memory fake would agree with whatever the seed happened to be in.
    /// </remarks>
    [Fact]
    public async Task GetSeriesForEpisodeAsync_ReachesTheWholeSeriesInOrder()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var seriesId = await StoreOneSeriesAsync(database, sourceId, SixPm, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.SaveSeriesDetailAsync(
                seriesId,
                new SeriesDetail(
                    [
                        SeasonWith(2, Episode("2001", "Seven Thirty-Seven", 1)),
                        SeasonWith(1, Episode("1002", "Cat's in the Bag...", 2), Episode("1001", "Pilot", 1)),
                    ]),
                SixPm,
                SixPm,
                cancellationToken);
        }

        var pilotId = await EpisodeIdAsync(database, "1001", cancellationToken);

        // Act
        await using var verifyContext = database.CreateContext();
        var series = await verifyContext.GetSeriesForEpisodeAsync(pilotId, cancellationToken);

        // Assert
        series.ShouldNotBeNull();
        series.Id.ShouldBe(seriesId);
        series.Seasons.Select(season => season.Number).ShouldBe([1, 2]);

        EpisodeSequence
            .Neighbour(series, pilotId, offset: 1)
            .ShouldNotBeNull()
            .Episode.ExternalId.ShouldBe("1002");
    }

    [Fact]
    public async Task GetSeriesForEpisodeAsync_ForAnEpisodeThatHasGone_ReportsNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        await using var context = database.CreateContext();

        // Act
        var series = await context.GetSeriesForEpisodeAsync(episodeId: 4242, cancellationToken);

        // Assert
        series.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteSourceAsync_TakesTheFilmsSeriesSeasonsAndEpisodesWithIt()
    {
        // Arrange: through the cascade, so no explicit cleanup exists to be forgotten.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var (_, seriesId) = await StoreCatalogueAsync(
            database,
            sourceId,
            withMovie: true,
            withSeries: true,
            cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.SaveSeriesDetailAsync(
                seriesId,
                new SeriesDetail([SeasonWith(1, Episode("1001", "Pilot", 1))]),
                SixPm,
                SixPm,
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.DeleteSourceAsync(sourceId, cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        (await verifyContext.Movies.CountAsync(cancellationToken)).ShouldBe(0);
        (await verifyContext.Series.CountAsync(cancellationToken)).ShouldBe(0);
        (await verifyContext.Seasons.CountAsync(cancellationToken)).ShouldBe(0);
        (await verifyContext.Episodes.CountAsync(cancellationToken)).ShouldBe(0);
    }
}
