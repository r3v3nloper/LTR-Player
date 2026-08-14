using LTR.Core;
using LTR.Core.Content;
using LTR.Core.Security;
using LTR.Core.Sources;
using LTR.Persistence;
using LTR.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LTR.Catalogue;

/// <summary>
/// Covers the import sequence that previously existed three times over, in the window twice and in the
/// command line tool once.
/// </summary>
public sealed class SourceImportServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private ServiceProvider? _services;

    [Fact]
    public async Task ImportAsync_ChecksTheAccountBeforeFetchingAnything()
    {
        // Arrange: the order is the point. Fetching before checking would report an expired subscription
        // as an empty catalogue, and probing after fetching would be useless since capabilities decide
        // what can be fetched.
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source);
        registry.Channels.Add(CreateChannel("101", "Erste"));

        var import = await CreateServiceAsync(registry, cancellationToken);

        // Act
        await import.ImportAsync(source, progress: null, cancellationToken);

        // Assert
        registry.Calls.ShouldBe(["authenticate", "probe", "categories:Live", "channels"]);
    }

    [Fact]
    public async Task ImportAsync_WhenTheAccountIsUnusable_StoresNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source) { Account = ProviderAccount.Unauthenticated };
        registry.Channels.Add(CreateChannel("101", "Erste"));

        var import = await CreateServiceAsync(registry, cancellationToken);

        // Act
        var result = await import.ImportAsync(source, progress: null, cancellationToken);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.SourceId.ShouldBe(0);
        registry.Calls.ShouldBe(["authenticate"], "nothing is fetched for an account that cannot be used");

        var store = _services!.GetRequiredService<ICatalogueStore>();
        (await store.GetSourcesAsync(cancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ImportAsync_StoresTheSourceItsCapabilitiesAndItsCatalogue()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source)
        {
            Capabilities = new ProviderCapabilities { SupportsLive = true, SupportsXmltvEpg = true },
        };

        registry.Categories.Add(CreateCategory("10", "Sport"));
        registry.Channels.Add(CreateChannel("101", "Erste", "10"));
        registry.Channels.Add(CreateChannel("102", "Zweite", "10"));

        var import = await CreateServiceAsync(registry, cancellationToken);

        // Act
        var result = await import.ImportAsync(source, progress: null, cancellationToken);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.SourceId.ShouldBeGreaterThan(0);
        result.ChannelCount.ShouldBe(2);
        result.CategoryCount.ShouldBe(1);

        var store = _services!.GetRequiredService<ICatalogueStore>();
        var stored = (await store.GetSourcesAsync(cancellationToken)).ShouldHaveSingleItem();

        stored.Capabilities.SupportsXmltvEpg.ShouldBeTrue("the probe result is persisted with the source");
        (await store.GetLiveChannelsAsync(stored.Id, cancellationToken)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task ImportAsync_ReportsEveryStageInOrder()
    {
        // Arrange: the window turns these into status text, so a missing or reordered stage shows up as
        // a wrong message rather than as a failure.
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source);
        var import = await CreateServiceAsync(registry, cancellationToken);

        var stages = new List<SourceImportStage>();
        var progress = new SynchronousProgress<SourceImportStage>(stages.Add);

        // Act
        await import.ImportAsync(source, progress, cancellationToken);

        // Assert
        stages.ShouldBe(
        [
            SourceImportStage.Authenticating,
            SourceImportStage.Probing,
            SourceImportStage.FetchingCatalogue,
            SourceImportStage.Storing,
        ]);
    }

    [Fact]
    public async Task RefreshAsync_KeepsFavouritesWhileReplacingProviderOwnedData()
    {
        // Arrange: the reason refresh goes through the same reconciliation as an import.
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source);
        registry.Channels.Add(CreateChannel("101", "Erste"));

        var import = await CreateServiceAsync(registry, cancellationToken);
        var store = _services!.GetRequiredService<ICatalogueStore>();

        var imported = await import.ImportAsync(source, progress: null, cancellationToken);
        var channel = (await store.GetLiveChannelsAsync(imported.SourceId, cancellationToken)).Single();
        await store.SetFavoriteAsync(channel.Id, isFavorite: true, cancellationToken);

        // Act: the provider comes back with the channel renamed.
        registry.Channels.Clear();
        registry.Channels.Add(CreateChannel("101", "Erste HD"));
        await import.RefreshAsync(source, progress: null, cancellationToken);

        // Assert
        var refreshed = (await store.GetLiveChannelsAsync(imported.SourceId, cancellationToken)).Single();
        refreshed.Name.ShouldBe("Erste HD");
        refreshed.IsFavorite.ShouldBeTrue();
    }

    [Fact]
    public async Task RefreshAsync_DoesNotStoreTheSourceASecondTime()
    {
        // Arrange: a refresh must update in place, not add a duplicate.
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source);
        var import = await CreateServiceAsync(registry, cancellationToken);

        await import.ImportAsync(source, progress: null, cancellationToken);

        // Act
        await import.RefreshAsync(source, progress: null, cancellationToken);

        // Assert
        var store = _services!.GetRequiredService<ICatalogueStore>();
        (await store.GetSourcesAsync(cancellationToken)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task ImportAsync_WhenThePanelOffersFilmsAndSeries_FetchesAndStoresThemToo()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source)
        {
            Capabilities = new ProviderCapabilities
            {
                SupportsLive = true,
                SupportsVod = true,
                SupportsSeries = true,
            },
        };

        registry.Categories.Add(CreateCategory("10", "Sport", ContentKind.Live));
        registry.Categories.Add(CreateCategory("58", "Action", ContentKind.Movie));
        registry.Categories.Add(CreateCategory("75", "Drama", ContentKind.Series));
        registry.Channels.Add(CreateChannel("101", "Erste", "10"));
        registry.Movies.Add(CreateMovie("8412", "Arrival", "58"));
        registry.Series.Add(CreateSeries("4321", "Breaking Bad", "75"));

        var import = await CreateServiceAsync(registry, cancellationToken);

        // Act
        var result = await import.ImportAsync(source, progress: null, cancellationToken);

        // Assert
        result.MovieCount.ShouldBe(1);
        result.SeriesCount.ShouldBe(1);
        result.CategoryCount.ShouldBe(3, "across every kind the import covered");
        result.HasVod.ShouldBeTrue();

        registry.Calls.ShouldBe(
        [
            "authenticate",
            "probe",
            "categories:Live",
            "channels",
            "categories:Movie",
            "movies",
            "categories:Series",
            "series",
        ]);

        var store = _services!.GetRequiredService<ICatalogueStore>();
        (await store.GetMoviesAsync(result.SourceId, cancellationToken))
            .ShouldHaveSingleItem()
            .Name.ShouldBe("Arrival");
        (await store.GetSeriesAsync(result.SourceId, cancellationToken))
            .ShouldHaveSingleItem()
            .Name.ShouldBe("Breaking Bad");
    }

    /// <summary>
    /// Panels that lack these endpoints answer them with the authentication object or with an HTML error
    /// page, so asking anyway would put two failed requests into every refresh of every playlist source.
    /// </summary>
    [Fact]
    public async Task ImportAsync_WhenThePanelOffersNoFilmsOrSeries_DoesNotAskForThem()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source)
        {
            Capabilities = new ProviderCapabilities { SupportsLive = true },
        };

        var import = await CreateServiceAsync(registry, cancellationToken);

        // Act
        var result = await import.ImportAsync(source, progress: null, cancellationToken);

        // Assert
        registry.Calls.ShouldNotContain("movies");
        registry.Calls.ShouldNotContain("series");
        result.HasVod.ShouldBeFalse();
    }

    [Fact]
    public async Task ImportAsync_WithFilms_ReportsTheFilmStageBeforeStoring()
    {
        // Arrange: on a subscription that offers them this is the longest step of an import, which is why
        // it is reported at all.
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source)
        {
            Capabilities = new ProviderCapabilities { SupportsLive = true, SupportsVod = true },
        };

        var import = await CreateServiceAsync(registry, cancellationToken);
        var stages = new List<SourceImportStage>();

        // Act
        await import.ImportAsync(source, new SynchronousProgress<SourceImportStage>(stages.Add), cancellationToken);

        // Assert
        stages.ShouldBe(
        [
            SourceImportStage.Authenticating,
            SourceImportStage.Probing,
            SourceImportStage.FetchingCatalogue,
            SourceImportStage.FetchingVod,
            SourceImportStage.Storing,
        ]);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheSubscriptionStopsOfferingAFilm_RemovesIt()
    {
        // Arrange: reconciled on every import even when empty, so a withdrawn section does not leave an
        // unplayable catalogue behind.
        var cancellationToken = TestContext.Current.CancellationToken;
        var source = CreateSource();
        var registry = new FakeProviderRegistry(source)
        {
            Capabilities = new ProviderCapabilities { SupportsLive = true, SupportsVod = true },
        };

        registry.Movies.Add(CreateMovie("8412", "Arrival"));

        var import = await CreateServiceAsync(registry, cancellationToken);
        var imported = await import.ImportAsync(source, progress: null, cancellationToken);

        // Act
        registry.Movies.Clear();
        await import.RefreshAsync(source, progress: null, cancellationToken);

        // Assert
        var store = _services!.GetRequiredService<ICatalogueStore>();
        (await store.GetMoviesAsync(imported.SourceId, cancellationToken)).ShouldBeEmpty();
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _connection.DisposeAsync();
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

    private static Category CreateCategory(
        string externalId,
        string name,
        ContentKind kind = ContentKind.Live)
    {
        return new Category { ExternalId = externalId, Name = name, Kind = kind };
    }

    private static VodItem CreateMovie(string externalId, string name, string? categoryExternalId = null)
    {
        return new VodItem
        {
            ExternalId = externalId,
            Name = name,
            CategoryExternalId = categoryExternalId,
        };
    }

    private static Series CreateSeries(string externalId, string name, string? categoryExternalId = null)
    {
        return new Series
        {
            ExternalId = externalId,
            Name = name,
            CategoryExternalId = categoryExternalId,
        };
    }

    private static Channel CreateChannel(string externalId, string name, string? categoryExternalId = null)
    {
        return new Channel
        {
            ExternalId = externalId,
            Name = name,
            CategoryExternalId = categoryExternalId,
        };
    }

    private async Task<ISourceImportService> CreateServiceAsync(
        IProviderRegistry registry,
        CancellationToken cancellationToken)
    {
        await _connection.OpenAsync(cancellationToken);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ICredentialProtector, PassThroughCredentialProtector>();
        services.AddSingleton(registry);
        services.AddDbContext<LtrDbContext>(options => options.UseSqlite(_connection));
        services.AddSingleton<CatalogueUnitOfWork>();
        services.AddSingleton<ICatalogueStore, CatalogueStore>();
        services.AddSingleton<ISourceImportService, SourceImportService>();

        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken);

        return _services.GetRequiredService<ISourceImportService>();
    }
}
