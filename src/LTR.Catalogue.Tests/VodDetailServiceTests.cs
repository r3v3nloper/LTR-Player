using LTR.Core;
using LTR.Core.Content;
using LTR.Core.Security;
using LTR.Core.Sources;
using LTR.Persistence;
using LTR.Providers;
using LTR.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LTR.Catalogue;

/// <summary>
/// Covers when a series or film detail is fetched and when the stored copy is served instead.
/// </summary>
/// <remarks>
/// This decision is the whole reason the service exists. A subscription lists thousands of series and
/// each one's seasons take a call of their own, so fetching too eagerly makes the catalogue unusable —
/// and fetching too rarely shows a season that stops an episode short.
/// </remarks>
public sealed class VodDetailServiceTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private readonly TestClock _clock = new(Noon);
    private ServiceProvider? _services;

    [Fact]
    public async Task GetSeriesAsync_WhenNothingHasBeenFetched_FetchesTheSeasonsAndStoresThem()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source)
        {
            SeriesDetail = new SeriesDetail(
                [Season(1, Episode("1001", "Pilot", 1)), Season(2, Episode("2001", "Later", 1))],
                Plot: "A chemistry teacher."),
        };

        var (detailService, seriesId) = await CreateWithSeriesAsync(registry, source, cancellationToken);

        // Act
        var series = await detailService.GetSeriesAsync(source, seriesId, cancellationToken);

        // Assert
        series.ShouldNotBeNull();
        series.Plot.ShouldBe("A chemistry teacher.");
        series.Seasons.Count.ShouldBe(2);
        series.Seasons.First().Episodes.ShouldHaveSingleItem().Title.ShouldBe("Pilot");
        registry.Calls.ShouldContain("series-detail:4321");
    }

    [Fact]
    public async Task GetSeriesAsync_WhenTheStoredSeasonsAreCurrent_DoesNotAskThePanelAgain()
    {
        // Arrange: the reason the fetch is cached at all. Opening a series twice must not cost two calls.
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source)
        {
            SeriesDetail = new SeriesDetail([Season(1, Episode("1001", "Pilot", 1))]),
        };

        var (detailService, seriesId) = await CreateWithSeriesAsync(registry, source, cancellationToken);
        await detailService.GetSeriesAsync(source, seriesId, cancellationToken);

        // Act
        var series = await detailService.GetSeriesAsync(source, seriesId, cancellationToken);

        // Assert
        series!.Seasons.ShouldHaveSingleItem();
        registry.Calls.Count(call => call == "series-detail:4321").ShouldBe(1);
    }

    [Fact]
    public async Task GetSeriesAsync_WhenTheProviderReportsTheSeriesChanged_FetchesAgain()
    {
        // Arrange: a series gaining an episode moves last_modified, and that is the only signal that the
        // stored seasons are a season short.
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source)
        {
            SeriesDetail = new SeriesDetail([Season(1, Episode("1001", "Pilot", 1))]),
        };

        var (detailService, seriesId) = await CreateWithSeriesAsync(registry, source, cancellationToken);
        await detailService.GetSeriesAsync(source, seriesId, cancellationToken);

        // The provider now reports a newer series with a second episode.
        registry.Series[0].LastModifiedUtc = Noon.AddDays(1);
        registry.SeriesDetail = new SeriesDetail(
            [Season(1, Episode("1001", "Pilot", 1), Episode("1002", "Second", 2))]);

        await _services!.GetRequiredService<ISourceImportService>()
            .RefreshAsync(source, progress: null, cancellationToken);

        // Act
        var series = await detailService.GetSeriesAsync(source, seriesId, cancellationToken);

        // Assert
        series!.Seasons.ShouldHaveSingleItem().Episodes.Count.ShouldBe(2);
        registry.Calls.Count(call => call == "series-detail:4321").ShouldBe(2);
    }

    [Fact]
    public async Task GetSeriesAsync_WhenThePanelCannotBeReached_ServesWhatIsStored()
    {
        // Arrange: this runs because the user opened a series, and last week's episode list is far better
        // than an error where the episodes should be.
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source)
        {
            SeriesDetail = new SeriesDetail([Season(1, Episode("1001", "Pilot", 1))]),
        };

        var (detailService, seriesId) = await CreateWithSeriesAsync(registry, source, cancellationToken);
        await detailService.GetSeriesAsync(source, seriesId, cancellationToken);

        registry.Series[0].LastModifiedUtc = Noon.AddDays(1);
        await _services!.GetRequiredService<ISourceImportService>()
            .RefreshAsync(source, progress: null, cancellationToken);

        registry.DetailFetchFails = true;

        // Act
        var series = await detailService.GetSeriesAsync(source, seriesId, cancellationToken);

        // Assert
        series.ShouldNotBeNull();
        series.Seasons.ShouldHaveSingleItem().Episodes.ShouldHaveSingleItem().Title.ShouldBe("Pilot");
    }

    [Fact]
    public async Task GetSeriesAsync_WhenTheSeriesIsGone_ReportsNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source);
        var (detailService, _) = await CreateWithSeriesAsync(registry, source, cancellationToken);

        // Act
        var series = await detailService.GetSeriesAsync(source, seriesId: 999, cancellationToken);

        // Assert
        series.ShouldBeNull();
        registry.Calls.ShouldNotContain("series-detail:999");
    }

    [Fact]
    public async Task GetMovieAsync_FetchesTheDetailOnceAndKeepsIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source)
        {
            MovieDetail = new MovieDetail(
                Plot: "Linguist meets heptapods.",
                DurationSeconds: 6_960,
                ContainerExtension: "mkv"),
        };

        var (detailService, movieId) = await CreateWithMovieAsync(registry, source, cancellationToken);

        // Act
        var first = await detailService.GetMovieAsync(source, movieId, cancellationToken);
        var second = await detailService.GetMovieAsync(source, movieId, cancellationToken);

        // Assert
        first!.Plot.ShouldBe("Linguist meets heptapods.");
        first.ContainerExtension.ShouldBe("mkv", "the container is what the film's address is built from");
        second!.Plot.ShouldBe(first.Plot);
        registry.Calls.Count(call => call == "movie-detail:8412").ShouldBe(1);
    }

    [Fact]
    public async Task GetMovieAsync_WhenThePanelHasNoDetail_StillServesTheListingEntry()
    {
        // Arrange: a panel answering with nothing is the common case for an older catalogue.
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source) { MovieDetail = null };
        var (detailService, movieId) = await CreateWithMovieAsync(registry, source, cancellationToken);

        // Act
        var movie = await detailService.GetMovieAsync(source, movieId, cancellationToken);

        // Assert
        movie.ShouldNotBeNull();
        movie.Name.ShouldBe("Arrival");
        movie.HasDetail.ShouldBeFalse("nothing was stored, so a later attempt is still worth making");
    }

    [Fact]
    public async Task GetMovieAsync_WhenThePanelHasNoDetail_DoesNotAskAgainOnTheNextViewing()
    {
        // Arrange: the empty answer used to leave nothing behind, so every viewing asked again — for every
        // film in a catalogue of tens of thousands, on a panel that has no detail for any of them.
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source) { MovieDetail = null };
        var (detailService, movieId) = await CreateWithMovieAsync(registry, source, cancellationToken);

        await detailService.GetMovieAsync(source, movieId, cancellationToken);

        // Act
        var movie = await detailService.GetMovieAsync(source, movieId, cancellationToken);

        // Assert
        movie.ShouldNotBeNull();
        registry.Calls.Count(call => call == "movie-detail:8412").ShouldBe(1);
    }

    [Fact]
    public async Task GetMovieAsync_WhenTheEmptyAnswerIsADayOld_AsksAgain()
    {
        // Arrange: an empty answer today is not proof of an empty answer next week. Panels do fill their
        // detail in, and a film whose synopsis arrives eventually has to be able to pick it up.
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source) { MovieDetail = null };
        var (detailService, movieId) = await CreateWithMovieAsync(registry, source, cancellationToken);

        await detailService.GetMovieAsync(source, movieId, cancellationToken);

        _clock.Advance(VodItem.DetailRetryInterval);
        registry.MovieDetail = new MovieDetail(Plot: "Linguist meets heptapods.");

        // Act
        var movie = await detailService.GetMovieAsync(source, movieId, cancellationToken);

        // Assert
        movie!.Plot.ShouldBe("Linguist meets heptapods.");
        registry.Calls.Count(call => call == "movie-detail:8412").ShouldBe(2);
    }

    [Fact]
    public async Task GetMovieAsync_WhenThePanelCouldNotBeReached_AsksAgainOnTheNextViewing()
    {
        // Arrange: the distinction the attempt column has to make. A momentary outage is not an answer, and
        // remembering it as one would suppress the retry for a day over nothing.
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source) { DetailFetchFails = true };
        var (detailService, movieId) = await CreateWithMovieAsync(registry, source, cancellationToken);

        await detailService.GetMovieAsync(source, movieId, cancellationToken);

        registry.DetailFetchFails = false;
        registry.MovieDetail = new MovieDetail(Plot: "Linguist meets heptapods.");

        // Act
        var movie = await detailService.GetMovieAsync(source, movieId, cancellationToken);

        // Assert
        movie!.Plot.ShouldBe("Linguist meets heptapods.");
        registry.Calls.Count(call => call == "movie-detail:8412").ShouldBe(2);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _connection.DisposeAsync();
    }

    private async Task<(IVodDetailService Service, int SeriesId)> CreateWithSeriesAsync(
        FakeProviderRegistry registry,
        XtreamSource source,
        CancellationToken cancellationToken)
    {
        registry.Capabilities = new ProviderCapabilities { SupportsLive = true, SupportsSeries = true };

        var series = new Series { ExternalId = "4321", Name = "Breaking Bad", LastModifiedUtc = Noon };
        registry.Series.Add(series);

        var import = await CreateServiceAsync(registry, cancellationToken);
        var result = await import.ImportAsync(source, progress: null, cancellationToken);

        var store = _services!.GetRequiredService<CatalogueStore>();
        var stored = (await store.GetSeriesAsync(result.SourceId, cancellationToken)).Single();

        return (_services!.GetRequiredService<IVodDetailService>(), stored.Id);
    }

    private async Task<(IVodDetailService Service, int MovieId)> CreateWithMovieAsync(
        FakeProviderRegistry registry,
        XtreamSource source,
        CancellationToken cancellationToken)
    {
        registry.Capabilities = new ProviderCapabilities { SupportsLive = true, SupportsVod = true };
        registry.Movies.Add(new VodItem { ExternalId = "8412", Name = "Arrival" });

        var import = await CreateServiceAsync(registry, cancellationToken);
        var result = await import.ImportAsync(source, progress: null, cancellationToken);

        var store = _services!.GetRequiredService<CatalogueStore>();
        var stored = (await store.GetMoviesAsync(result.SourceId, cancellationToken)).Single();

        return (_services!.GetRequiredService<IVodDetailService>(), stored.Id);
    }

    private async Task<ISourceImportService> CreateServiceAsync(
        IProviderRegistry registry,
        CancellationToken cancellationToken)
    {
        await _connection.OpenAsync(cancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<ICredentialProtector, PassThroughCredentialProtector>();
        services.AddSingleton(registry);
        services.AddDbContext<LtrDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<CatalogueUnitOfWork>();
        services.AddSingleton<CatalogueStore>();
        services.AddSingleton<ISourceImportService, SourceImportService>();
        services.AddSingleton<IVodDetailService, VodDetailService>();

        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken);

        return _services.GetRequiredService<ISourceImportService>();
    }

    private static XtreamSource CreateSource()
    {
        return new XtreamSource
        {
            Name = "Test source",
            BaseUrl = new Uri("http://panel.example:8080", UriKind.Absolute),
            Username = "alice",
            Password = "s3cret",
            CreatedUtc = DateTimeOffset.UnixEpoch,
        };
    }

    private static Season Season(int number, params Episode[] episodes)
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
