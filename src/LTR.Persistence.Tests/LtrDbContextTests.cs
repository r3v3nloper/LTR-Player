using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.EntityFrameworkCore;

namespace LTR.Persistence;

public sealed class LtrDbContextTests
{
    private static readonly DateTimeOffset RefreshedAt = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddSourceAsync_StoresThePasswordProtectedButReturnsItUsable()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var source = CreateXtreamSource(password: "s3cret");

        // Act
        await using (var context = database.CreateContext())
        {
            await context.AddSourceAsync(source, cancellationToken);
        }

        // Assert: the instance handed back is usable, the stored row is not the plaintext.
        source.Password.ShouldBe("s3cret");

        await using var verifyContext = database.CreateContext();
        var storedPassword = await verifyContext.Sources
            .AsNoTracking()
            .OfType<XtreamSource>()
            .Select(entity => entity.Password)
            .SingleAsync(cancellationToken);

        storedPassword.ShouldBe("terc3s", "the protected form reaches the database");
    }

    [Fact]
    public async Task AddSourceAsync_FollowedByAnotherSave_DoesNotLeakThePlaintextIntoTheDatabase()
    {
        // Arrange: revealing the password on a tracked entity would make the next save overwrite the
        // protected value with the plaintext. This is the regression guard for exactly that.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var source = CreateXtreamSource(password: "s3cret");

        // Act
        await using (var context = database.CreateContext())
        {
            var sourceId = await context.AddSourceAsync(source, cancellationToken);

            await context.UpdateCapabilitiesAsync(
                sourceId,
                new ProviderCapabilities { SupportsLive = true, ProbedAtUtc = RefreshedAt },
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var storedPassword = await verifyContext.Sources
            .AsNoTracking()
            .OfType<XtreamSource>()
            .Select(entity => entity.Password)
            .SingleAsync(cancellationToken);

        storedPassword.ShouldBe("terc3s");
    }

    [Fact]
    public async Task GetSourcesAsync_ReturnsCredentialsReadyForUse()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.AddSourceAsync(CreateXtreamSource(password: "s3cret"), cancellationToken);
        }

        // Act
        await using var readContext = database.CreateContext();
        var sources = await readContext.GetSourcesAsync(cancellationToken);

        // Assert
        var stored = sources.ShouldHaveSingleItem().ShouldBeOfType<XtreamSource>();
        stored.Password.ShouldBe("s3cret");
        stored.BaseUrl.ShouldBe(new Uri("http://panel.example:8080"));
    }

    [Fact]
    public async Task UpdateCapabilitiesAsync_PersistsTheProbeResult()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        int sourceId;

        await using (var context = database.CreateContext())
        {
            sourceId = await context.AddSourceAsync(CreateXtreamSource(), cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.UpdateCapabilitiesAsync(
                sourceId,
                new ProviderCapabilities
                {
                    SupportsLive = true,
                    SupportsMpegTs = true,
                    RequiresLivePathSegment = true,
                    ProbedAtUtc = RefreshedAt,
                },
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var sources = await verifyContext.GetSourcesAsync(cancellationToken);
        var capabilities = sources.ShouldHaveSingleItem().Capabilities;

        capabilities.HasBeenProbed.ShouldBeTrue();
        capabilities.SupportsLive.ShouldBeTrue();
        capabilities.SupportsMpegTs.ShouldBeTrue();
        capabilities.RequiresLivePathSegment.ShouldBeTrue();
    }

    [Fact]
    public async Task ReconcileLiveCatalogueAsync_StoresCategoriesAndLinksChannelsToThem()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        // Act
        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                sourceId,
                [Category("10", "Sport"), Category("20", "News")],
                [Channel("101", "Sport 1", categoryExternalId: "10")],
                RefreshedAt,
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var categories = await verifyContext.GetLiveCategoriesAsync(sourceId, cancellationToken);
        var channels = await verifyContext.GetLiveChannelsAsync(sourceId, cancellationToken);
        var sportCategoryId = categories.Single(category => category.ExternalId == "10").Id;

        categories.Count.ShouldBe(2);
        channels.ShouldHaveSingleItem().CategoryId.ShouldBe(sportCategoryId);
    }

    [Fact]
    public async Task ReconcileLiveCatalogueAsync_KeepsTheFavoriteFlagAcrossARefresh()
    {
        // Arrange: the favourite is the user's own data. The provider owns everything else about the
        // channel, so a refresh must overwrite the rest and leave this alone.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                sourceId,
                [Category("10", "Sport")],
                [Channel("101", "Sport 1", categoryExternalId: "10")],
                RefreshedAt,
                cancellationToken);

            var channelId = (await context.GetLiveChannelsAsync(sourceId, cancellationToken))
                .Single()
                .Id;

            await context.SetFavoriteAsync(channelId, isFavorite: true, cancellationToken);
        }

        // Act: the provider comes back with a renamed channel.
        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                sourceId,
                [Category("10", "Sport HD")],
                [Channel("101", "Sport 1 HD", categoryExternalId: "10")],
                RefreshedAt.AddHours(1),
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var channel = (await verifyContext.GetLiveChannelsAsync(sourceId, cancellationToken)).ShouldHaveSingleItem();

        channel.Name.ShouldBe("Sport 1 HD", "provider-owned data is refreshed");
        channel.IsFavorite.ShouldBeTrue("user-owned data survives the refresh");
    }

    [Fact]
    public async Task ReconcileLiveCatalogueAsync_RemovesEntriesTheProviderNoLongerOffers()
    {
        // Arrange: a shrinking subscription must not leave unplayable channels behind.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                sourceId,
                [Category("10", "Sport"), Category("20", "News")],
                [Channel("101", "Sport 1", "10"), Channel("102", "News 24", "20")],
                RefreshedAt,
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                sourceId,
                [Category("10", "Sport")],
                [Channel("101", "Sport 1", "10")],
                RefreshedAt.AddHours(1),
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var categories = await verifyContext.GetLiveCategoriesAsync(sourceId, cancellationToken);
        var channels = await verifyContext.GetLiveChannelsAsync(sourceId, cancellationToken);

        categories.ShouldHaveSingleItem().ExternalId.ShouldBe("10");
        channels.ShouldHaveSingleItem().ExternalId.ShouldBe("101");
    }

    [Fact]
    public async Task ReconcileLiveCatalogueAsync_WhenAChannelReferencesAnUnknownCategory_LeavesItUncategorised()
    {
        // Arrange: panels do reference categories they omit from the category list.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        // Act
        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                sourceId,
                [Category("10", "Sport")],
                [Channel("101", "Orphan", categoryExternalId: "999")],
                RefreshedAt,
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var channel = (await verifyContext.GetLiveChannelsAsync(sourceId, cancellationToken)).ShouldHaveSingleItem();

        channel.CategoryId.ShouldBeNull();
        channel.CategoryExternalId.ShouldBe("999", "the provider's own value is retained for the next refresh");
    }

    [Fact]
    public async Task ReconcileLiveCatalogueAsync_RecordsWhenTheRefreshHappened()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        // Act
        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(sourceId, [], [], RefreshedAt, cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var sources = await verifyContext.GetSourcesAsync(cancellationToken);

        sources.ShouldHaveSingleItem().LastRefreshedUtc.ShouldBe(RefreshedAt);
    }

    [Fact]
    public async Task GetLiveChannelsAsync_ReturnsTheProviderOrdering()
    {
        // Arrange: providers order their list deliberately and users expect that order.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);
        var sourceId = await AddSourceAsync(database, cancellationToken);

        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                sourceId,
                [],
                [
                    Channel("103", "Third", sortOrder: 2),
                    Channel("101", "First", sortOrder: 0),
                    Channel("102", "Second", sortOrder: 1),
                ],
                RefreshedAt,
                cancellationToken);
        }

        // Act
        await using var verifyContext = database.CreateContext();
        var channels = await verifyContext.GetLiveChannelsAsync(sourceId, cancellationToken);

        // Assert
        channels.Select(channel => channel.Name).ShouldBe(["First", "Second", "Third"]);
    }

    [Fact]
    public async Task ReconcileLiveCatalogueAsync_KeepsSourcesIndependentOfEachOther()
    {
        // Arrange: provider identifiers collide across subscriptions, so reconciling one source must
        // not touch another's catalogue.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var database = await SqliteTestDatabase.CreateAsync(cancellationToken: cancellationToken);

        int firstSourceId;
        int secondSourceId;

        await using (var context = database.CreateContext())
        {
            firstSourceId = await context.AddSourceAsync(CreateXtreamSource(name: "First"), cancellationToken);
            secondSourceId = await context.AddSourceAsync(CreateXtreamSource(name: "Second"), cancellationToken);
        }

        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                firstSourceId,
                [Category("10", "Sport")],
                [Channel("101", "From first", "10")],
                RefreshedAt,
                cancellationToken);
        }

        // Act
        await using (var context = database.CreateContext())
        {
            await context.ReconcileLiveCatalogueAsync(
                secondSourceId,
                [Category("10", "Sport")],
                [Channel("101", "From second", "10")],
                RefreshedAt,
                cancellationToken);
        }

        // Assert
        await using var verifyContext = database.CreateContext();
        var firstChannels = await verifyContext.GetLiveChannelsAsync(firstSourceId, cancellationToken);
        var secondChannels = await verifyContext.GetLiveChannelsAsync(secondSourceId, cancellationToken);

        firstChannels.ShouldHaveSingleItem().Name.ShouldBe("From first");
        secondChannels.ShouldHaveSingleItem().Name.ShouldBe("From second");
    }

    private static async Task<int> AddSourceAsync(
        SqliteTestDatabase database,
        CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext();
        return await context.AddSourceAsync(CreateXtreamSource(), cancellationToken);
    }

    private static XtreamSource CreateXtreamSource(string name = "Test source", string password = "pass")
    {
        return new XtreamSource
        {
            Name = name,
            BaseUrl = new Uri("http://panel.example:8080", UriKind.Absolute),
            Username = "alice",
            Password = password,
            CreatedUtc = RefreshedAt,
        };
    }

    private static Category Category(string externalId, string name, int sortOrder = 0)
    {
        return new Category
        {
            ExternalId = externalId,
            Name = name,
            Kind = ContentKind.Live,
            SortOrder = sortOrder,
        };
    }

    private static Channel Channel(
        string externalId,
        string name,
        string? categoryExternalId = null,
        int sortOrder = 0)
    {
        return new Channel
        {
            ExternalId = externalId,
            Name = name,
            CategoryExternalId = categoryExternalId,
            SortOrder = sortOrder,
        };
    }
}
