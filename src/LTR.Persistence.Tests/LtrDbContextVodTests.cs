using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using Microsoft.EntityFrameworkCore;

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
    private static readonly DateTimeOffset SixPm = new(2026, 8, 12, 18, 0, 0, TimeSpan.Zero);

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

    [Fact]
    public async Task RecordMovieProgressAsync_WhenFinished_ClearsThePositionAndMarksItWatched()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var movieId = await StoreOneMovieAsync(database, sourceId, cancellationToken);

        // Act
        await using (var context = database.CreateContext())
        {
            await context.RecordMovieProgressAsync(
                movieId,
                WatchOutcome.Finished,
                TimeSpan.FromMinutes(99),
                SixPm,
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var movie = await verifyContext.GetMovieAsync(movieId, cancellationToken);
        movie.ShouldNotBeNull();
        movie.ResumePositionSeconds.ShouldBeNull("resuming at the closing credits offers nothing");
        movie.IsWatched.ShouldBeTrue();
        movie.LastWatchedUtc.ShouldBe(SixPm);
    }

    [Fact]
    public async Task ForgetMovieProgressAsync_ClearsThePositionWithoutTouchingWhenItWasWatched()
    {
        // Arrange: a film watched to 40 minutes at six, then taken off the list by the viewer. Expressed as
        // a discarding outcome — which is how both front ends used to do it — this stamped LastWatchedUtc
        // with the moment of removal, so the entry came back as the most recently watched thing they own.
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
            await context.ForgetMovieProgressAsync(movieId, cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var movie = await verifyContext.GetMovieAsync(movieId, cancellationToken);
        movie.ShouldNotBeNull();
        movie.ResumePositionSeconds.ShouldBeNull("the position is what the viewer asked to forget");
        movie.LastWatchedUtc.ShouldBe(SixPm, "removing an entry is not watching it");
    }

    [Fact]
    public async Task ForgetMovieProgressAsync_DoesNotUnwatchAFilmAlreadyFinished()
    {
        // Arrange: forgetting where you got to in a film you had already finished does not unfinish it.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var movieId = await StoreOneMovieAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.RecordMovieProgressAsync(
                movieId,
                WatchOutcome.Finished,
                TimeSpan.FromMinutes(99),
                SixPm,
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.ForgetMovieProgressAsync(movieId, cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var movie = await verifyContext.GetMovieAsync(movieId, cancellationToken);
        movie.ShouldNotBeNull();
        movie.IsWatched.ShouldBeTrue();
    }

    [Fact]
    public async Task RecordMovieProgressAsync_WhenDiscarded_DoesNotUnwatchAFilmAlreadyFinished()
    {
        // Arrange: opening a film that was watched through and closing it again is not un-watching it.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var movieId = await StoreOneMovieAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.RecordMovieProgressAsync(
                movieId,
                WatchOutcome.Finished,
                TimeSpan.FromMinutes(99),
                SixPm,
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.RecordMovieProgressAsync(
                movieId,
                WatchOutcome.Discard,
                TimeSpan.FromSeconds(20),
                SixPm.AddHours(1),
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var movie = await verifyContext.GetMovieAsync(movieId, cancellationToken);
        movie!.IsWatched.ShouldBeTrue();
        movie.ResumePositionSeconds.ShouldBeNull();
    }

    [Fact]
    public async Task GetContinueWatchingAsync_ListsFilmsAndEpisodesTogetherMostRecentFirst()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var (movieId, seriesId) = await StoreCatalogueAsync(
            database,
            sourceId,
            withMovie: true,
            withSeries: true,
            cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.SaveSeriesDetailAsync(
                seriesId,
                new SeriesDetail([SeasonWith(2, Episode("2005", "Fly", 5))]),
                SixPm,
                SixPm,
                cancellationToken);
        }

        var episodeId = await SingleEpisodeIdAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.RecordMovieProgressAsync(
                movieId,
                WatchOutcome.Resumable,
                TimeSpan.FromMinutes(40),
                SixPm,
                cancellationToken);

            await context.RecordEpisodeProgressAsync(
                episodeId,
                WatchOutcome.Resumable,
                TimeSpan.FromMinutes(20),
                SixPm.AddHours(2),
                cancellationToken);
        }

        // Act
        await using var verifyContext = database.CreateContext();
        var entries = await verifyContext.GetContinueWatchingAsync(sourceId, limit: 10, cancellationToken);

        // Assert
        entries.Count.ShouldBe(2);

        var first = entries[0];
        first.Kind.ShouldBe(ContentKind.Series);
        first.ItemId.ShouldBe(episodeId, "the identity is the episode's, not the series'");
        first.Title.ShouldBe("Breaking Bad");
        first.Subtitle.ShouldBe("S02E05 · Fly");
        first.PositionSeconds.ShouldBe(1200);

        entries[1].Kind.ShouldBe(ContentKind.Movie);
        entries[1].ItemId.ShouldBe(movieId);
        entries[1].Subtitle.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetContinueWatchingAsync_OmitsWhatWasFinishedOrBarelyStarted()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);
        var movieId = await StoreOneMovieAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.RecordMovieProgressAsync(
                movieId,
                WatchOutcome.Finished,
                TimeSpan.FromMinutes(99),
                SixPm,
                cancellationToken);
        }

        // Act
        await using var verifyContext = database.CreateContext();
        var entries = await verifyContext.GetContinueWatchingAsync(sourceId, limit: 10, cancellationToken);

        // Assert
        entries.ShouldBeEmpty();
    }

    /// <summary>
    /// SQLite has no date type, and EF's default mapping for a <see cref="DateTimeOffset"/> appends the
    /// offset as text, which sorts wrongly. This list is ordered by an instant, so the converter on the
    /// column is what makes the order come out right rather than alphabetical.
    /// </summary>
    [Fact]
    public async Task GetContinueWatchingAsync_OrdersCorrectlyAcrossOffsets()
    {
        // Arrange: the same two instants, recorded in different offsets. Written as text with the offset
        // attached, "20:00+02:00" sorts after "19:00+00:00" although it is the earlier moment.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.ReconcileVodCatalogueAsync(
                sourceId,
                [],
                [Movie("1", "Earlier"), Movie("2", "Later")],
                [],
                cancellationToken);
        }

        var movies = await MovieIdsByNameAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.RecordMovieProgressAsync(
                movies["Earlier"],
                WatchOutcome.Resumable,
                TimeSpan.FromMinutes(10),
                new DateTimeOffset(2026, 8, 12, 20, 0, 0, TimeSpan.FromHours(2)),
                cancellationToken);

            await context.RecordMovieProgressAsync(
                movies["Later"],
                WatchOutcome.Resumable,
                TimeSpan.FromMinutes(10),
                new DateTimeOffset(2026, 8, 12, 19, 0, 0, TimeSpan.Zero),
                cancellationToken);
        }

        // Act
        await using var verifyContext = database.CreateContext();
        var entries = await verifyContext.GetContinueWatchingAsync(sourceId, limit: 10, cancellationToken);

        // Assert
        entries.Select(entry => entry.Title).ShouldBe(["Later", "Earlier"]);
    }

    [Fact]
    public async Task GetContinueWatchingAsync_HonoursTheLimit()
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
                [Movie("1", "One"), Movie("2", "Two"), Movie("3", "Three")],
                [],
                cancellationToken);
        }

        var movies = await MovieIdsByNameAsync(database, sourceId, cancellationToken);

        await using (var context = database.CreateContext())
        {
            var minute = 0;

            foreach (var movieId in movies.Values)
            {
                await context.RecordMovieProgressAsync(
                    movieId,
                    WatchOutcome.Resumable,
                    TimeSpan.FromMinutes(10),
                    SixPm.AddMinutes(minute++),
                    cancellationToken);
            }
        }

        // Act
        await using var verifyContext = database.CreateContext();
        var entries = await verifyContext.GetContinueWatchingAsync(sourceId, limit: 2, cancellationToken);

        // Assert
        entries.Count.ShouldBe(2);
    }

    private static async Task<int> AddSourceAsync(
        SqliteTestDatabase database,
        CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext();

        return await context.AddSourceAsync(
            new XtreamSource
            {
                Name = "Test source",
                BaseUrl = new Uri("http://panel.example:8080", UriKind.Absolute),
                Username = "alice",
                Password = "pass",
                CreatedUtc = SixPm,
            },
            cancellationToken);
    }

    private static async Task<int> StoreOneMovieAsync(
        SqliteTestDatabase database,
        int sourceId,
        CancellationToken cancellationToken)
    {
        return (await StoreCatalogueAsync(database, sourceId, withMovie: true, withSeries: false, cancellationToken))
            .MovieId;
    }

    private static async Task<int> StoreOneSeriesAsync(
        SqliteTestDatabase database,
        int sourceId,
        DateTimeOffset lastModifiedUtc,
        CancellationToken cancellationToken)
    {
        return (await StoreCatalogueAsync(
                database,
                sourceId,
                withMovie: false,
                withSeries: true,
                cancellationToken,
                lastModifiedUtc))
            .SeriesId;
    }

    /// <summary>
    /// Stores a catalogue in one pass.
    /// </summary>
    /// <remarks>
    /// One call rather than one per kind, because a reconciliation is authoritative: storing a film and
    /// then storing a series with an empty film list correctly deletes the film again.
    /// </remarks>
    private static async Task<(int MovieId, int SeriesId)> StoreCatalogueAsync(
        SqliteTestDatabase database,
        int sourceId,
        bool withMovie,
        bool withSeries,
        CancellationToken cancellationToken,
        DateTimeOffset? lastModifiedUtc = null)
    {
        await using var context = database.CreateContext();

        var series = SeriesEntry("4321", "Breaking Bad");
        series.LastModifiedUtc = lastModifiedUtc ?? SixPm;

        await context.ReconcileVodCatalogueAsync(
            sourceId,
            [],
            withMovie ? [Movie("1", "Arrival")] : [],
            withSeries ? [series] : [],
            cancellationToken);

        var movies = await context.GetMoviesAsync(sourceId, cancellationToken);
        var stored = await context.GetSeriesAsync(sourceId, cancellationToken);

        return (
            withMovie ? movies.Single().Id : 0,
            withSeries ? stored.Single().Id : 0);
    }

    private static async Task<int> EpisodeIdAsync(
        SqliteTestDatabase database,
        string externalId,
        CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext();

        return await context.Episodes
            .Where(episode => episode.ExternalId == externalId)
            .Select(episode => episode.Id)
            .SingleAsync(cancellationToken);
    }

    private static async Task<int> SingleEpisodeIdAsync(
        SqliteTestDatabase database,
        CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext();
        return await context.Episodes.Select(episode => episode.Id).SingleAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, int>> MovieIdsByNameAsync(
        SqliteTestDatabase database,
        int sourceId,
        CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext();

        return (await context.GetMoviesAsync(sourceId, cancellationToken))
            .ToDictionary(movie => movie.Name, movie => movie.Id);
    }

    private static Category Category(string externalId, string name, ContentKind kind)
    {
        return new Category { ExternalId = externalId, Name = name, Kind = kind };
    }

    private static Channel Channel(string externalId, string name, string? categoryExternalId = null)
    {
        return new Channel
        {
            ExternalId = externalId,
            Name = name,
            CategoryExternalId = categoryExternalId,
        };
    }

    private static VodItem Movie(string externalId, string name, string? categoryExternalId = null)
    {
        return new VodItem
        {
            ExternalId = externalId,
            Name = name,
            CategoryExternalId = categoryExternalId,
        };
    }

    private static Series SeriesEntry(string externalId, string name, string? categoryExternalId = null)
    {
        return new Series
        {
            ExternalId = externalId,
            Name = name,
            CategoryExternalId = categoryExternalId,
        };
    }

    private static Season SeasonWith(int number, params Episode[] episodes)
    {
        return new Season { Number = number, Episodes = [.. episodes] };
    }

    private static Episode Episode(string externalId, string title, int number)
    {
        return new Episode
        {
            ExternalId = externalId,
            Title = title,
            Number = number,
            ContainerExtension = "mkv",
        };
    }
}
