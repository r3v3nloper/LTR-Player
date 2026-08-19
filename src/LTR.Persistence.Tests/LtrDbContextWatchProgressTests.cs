using LTR.Core.Content;
using LTR.Core.Playback;
using Microsoft.EntityFrameworkCore;
using static LTR.Persistence.VodFixtures;

namespace LTR.Persistence;

/// <summary>
/// Where the viewer got to, against real SQLite.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>LtrDbContext.WatchProgress.cs</c>, which is the seam these came out from behind: a position
/// is the one thing in the catalogue the viewer owns, and every case here is about a write that must not
/// claim more than it knows — a removal that is not a viewing, a re-watch that must not unwatch.
/// </para>
/// <para>
/// Against a real database rather than in memory, because the continue-watching list is two queries and a
/// merge across tables that share nothing, and because <c>ExecuteUpdateAsync</c> is translated rather than
/// executed in C# — a rule expressed in a setter lambda is only a rule if SQLite agrees.
/// </para>
/// </remarks>
public sealed class LtrDbContextWatchProgressTests
{
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
}
