using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LTR.Persistence;

/// <summary>
/// Verifies that migrating an existing database keeps what is in it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MigrationTests"/> migrates an empty database, which proves the schema is buildable and
/// nothing else. An upgrade is the case that can lose data: SQLite cannot alter a constraint in place, so
/// EF implements one by rebuilding the table — create, copy, drop, rename — and a rebuild that copies the
/// wrong columns silently empties it. The user's catalogue is a cache and could be refetched, but their
/// favourites and their configured subscriptions could not.
/// </para>
/// <para>
/// One test per migration that alters an existing table, added as such migrations are written.
/// </para>
/// </remarks>
public sealed class MigrationUpgradeTests
{
    /// <summary>
    /// The last migration before the guide, which is the state a database upgraded to this build starts
    /// from.
    /// </summary>
    private const string BeforeGuide = "20260813082247_AddChannelStreamUrl";

    /// <summary>The last migration before films and series, which is what an M3-era installation holds.</summary>
    private const string BeforeVod = "20260813113737_AddGuide";

    /// <summary>
    /// The last migration before the film-detail attempt column, which is what every shipped 0.6.0 holds.
    /// </summary>
    private const string BeforeDetailAttempt = "20260814063550_AddVodCatalogue";

    /// <summary>A fixed instant, as the other persistence tests use.</summary>
    private static readonly DateTimeOffset SixPm = new(2026, 8, 12, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddGuide_KeepsTheSourcesChannelsAndFavouritesAlreadyStored()
    {
        // Arrange: a database at the previous schema, with a source and a favourited channel in it.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<LtrDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new LtrDbContext(options, new ReversingCredentialProtector()))
        {
            await context.GetService<IMigrator>().MigrateAsync(BeforeGuide, cancellationToken);
        }

        // Written as SQL rather than through the context, because the context speaks the current model and
        // the point of the test is a database written by the previous version. The password is in the form
        // the reversing protector produces, so the assertion afterwards proves it still unprotects.
        await ExecuteAsync(
            connection,
            """
            INSERT INTO Sources
                (Id, Name, UserAgent, PreferredStreamFormat, CreatedUtc, Protocol,
                 Capabilities_SupportsLive, Capabilities_SupportsVod, Capabilities_SupportsSeries,
                 Capabilities_SupportsXmltvEpg, Capabilities_SupportsShortEpg, Capabilities_SupportsMpegTs,
                 Capabilities_SupportsHls, Capabilities_RequiresLivePathSegment,
                 BaseUrl, Username, Password)
            VALUES
                (1, 'Existing subscription', 'VLC/3.0.21', 0, '1970-01-01 00:00:00+00:00', 'xtream',
                 1, 0, 0, 1, 1, 1, 0, 1,
                 'http://panel.example:8080/', 'alice', 'rev:terc3s');

            INSERT INTO Categories (Id, SourceId, ExternalId, Name, Kind, SortOrder)
            VALUES (1, 1, '10', 'Sport', 0, 0);

            INSERT INTO Channels
                (Id, SourceId, CategoryId, CategoryExternalId, ExternalId, Name, HasArchive, IsFavorite,
                 SortOrder)
            VALUES
                (1, 1, 1, '10', '101', 'Erste', 0, 1, 0),
                (2, 1, NULL, NULL, '102', 'Zweite', 0, 0, 1);
            """,
            cancellationToken);

        // Act
        await using (var context = new LtrDbContext(options, new ReversingCredentialProtector()))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        // Assert
        await using var verifyContext = new LtrDbContext(options, new ReversingCredentialProtector());

        var sources = await verifyContext.GetSourcesAsync(cancellationToken);
        var source = sources.ShouldHaveSingleItem();
        source.Name.ShouldBe("Existing subscription");
        source.ShouldBeOfType<XtreamSource>().Password.ShouldBe("s3cret", "the credential still unprotects");

        var channels = await verifyContext.GetLiveChannelsAsync(source.Id, cancellationToken);
        channels.Count.ShouldBe(2);
        channels.Count(channel => channel.IsFavorite).ShouldBe(1, "favourites are the user's own data");
        channels.Single(channel => channel.ExternalId == "101").CategoryId.ShouldNotBeNull();

        var categories = await verifyContext.GetCategoriesAsync(source.Id, ContentKind.Live, cancellationToken);
        categories.ShouldHaveSingleItem().Name.ShouldBe("Sport");

        // And the new schema is usable afterwards, not merely present.
        await verifyContext.EnsureGuideChannelsAsync(
            source.Id,
            [new GuideChannel { ExternalId = "erste.de", DisplayName = "Erste" }],
            cancellationToken);

        (await verifyContext.LinkChannelsToGuideAsync(source.Id, cancellationToken)).ShouldBe(1);
    }

    /// <summary>
    /// The film and series migration only creates tables, so nothing can be rebuilt and nothing lost. The
    /// case is here anyway, because "it only adds tables" is a claim about the generated migration rather
    /// than about the model, and the next one to alter a table will be written next to this.
    /// </summary>
    [Fact]
    public async Task AddVodCatalogue_KeepsTheCatalogueAndAcceptsFilmsAfterwards()
    {
        // Arrange: a database at the guide-era schema, with a source, a favourited channel and a guide.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<LtrDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new LtrDbContext(options, new ReversingCredentialProtector()))
        {
            await context.GetService<IMigrator>().MigrateAsync(BeforeVod, cancellationToken);
        }

        // Written as SQL, because the context speaks the current model and the point is a database written
        // by the previous version.
        await ExecuteAsync(
            connection,
            """
            INSERT INTO Sources
                (Id, Name, UserAgent, PreferredStreamFormat, CreatedUtc, Protocol,
                 Capabilities_SupportsLive, Capabilities_SupportsVod, Capabilities_SupportsSeries,
                 Capabilities_SupportsXmltvEpg, Capabilities_SupportsShortEpg, Capabilities_SupportsMpegTs,
                 Capabilities_SupportsHls, Capabilities_RequiresLivePathSegment,
                 BaseUrl, Username, Password)
            VALUES
                (1, 'Existing subscription', 'VLC/3.0.21', 0, '1970-01-01 00:00:00+00:00', 'xtream',
                 1, 1, 1, 1, 1, 1, 0, 1,
                 'http://panel.example:8080/', 'alice', 'rev:terc3s');

            INSERT INTO Categories (Id, SourceId, ExternalId, Name, Kind, SortOrder)
            VALUES (1, 1, '58', 'Sport', 0, 0);

            INSERT INTO Channels
                (Id, SourceId, CategoryId, CategoryExternalId, ExternalId, Name, HasArchive, IsFavorite,
                 SortOrder)
            VALUES (1, 1, 1, '58', '101', 'Erste', 0, 1, 0);

            INSERT INTO GuideChannels (Id, SourceId, ExternalId, DisplayName) VALUES (1, 1, 'erste.de', 'Erste');
            """,
            cancellationToken);

        // Act
        await using (var context = new LtrDbContext(options, new ReversingCredentialProtector()))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        // Assert
        await using var verifyContext = new LtrDbContext(options, new ReversingCredentialProtector());

        var source = (await verifyContext.GetSourcesAsync(cancellationToken)).ShouldHaveSingleItem();
        var channels = await verifyContext.GetLiveChannelsAsync(source.Id, cancellationToken);
        channels.ShouldHaveSingleItem().IsFavorite.ShouldBeTrue("favourites are the user's own data");
        (await verifyContext.GuideChannels.CountAsync(cancellationToken)).ShouldBe(1);

        // And the new schema is usable afterwards, not merely present. The live category keeps external id
        // "58" here on purpose: a film category of the same number must not collide with it.
        await verifyContext.ReconcileVodCatalogueAsync(
            source.Id,
            [new Category { ExternalId = "58", Name = "Action", Kind = ContentKind.Movie }],
            [new VodItem { ExternalId = "8412", Name = "Arrival", CategoryExternalId = "58" }],
            [new Series { ExternalId = "4321", Name = "Breaking Bad" }],
            cancellationToken);

        var movie = (await verifyContext.GetMoviesAsync(source.Id, cancellationToken)).ShouldHaveSingleItem();
        movie.CategoryId.ShouldNotBe(channels[0].CategoryId, "the film sits in the film category");

        var categories = await verifyContext.GetCategoriesAsync(source.Id, ContentKind.Live, cancellationToken);
        categories.ShouldHaveSingleItem().Name.ShouldBe("Sport", "the live category survived");
    }

    /// <summary>
    /// The first migration to alter a table holding user data, which is the case this file exists for.
    /// </summary>
    /// <remarks>
    /// It generated a plain <c>ADD COLUMN</c> rather than a table rebuild, so nothing should be at risk — but
    /// "it only adds a column" is a claim about the generated migration and not about the model, and a stored
    /// resume position is exactly what a rebuild would quietly drop. The film carries one here for that
    /// reason.
    /// </remarks>
    [Fact]
    public async Task AddMovieDetailAttempt_KeepsStoredFilmsAndTheirResumePositions()
    {
        // Arrange: a 0.6.0 database with a film the viewer is part-way through.
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<LtrDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var context = new LtrDbContext(options, new ReversingCredentialProtector()))
        {
            await context.GetService<IMigrator>().MigrateAsync(BeforeDetailAttempt, cancellationToken);
        }

        await ExecuteAsync(
            connection,
            """
            INSERT INTO Sources
                (Id, Name, UserAgent, PreferredStreamFormat, CreatedUtc, Protocol,
                 Capabilities_SupportsLive, Capabilities_SupportsVod, Capabilities_SupportsSeries,
                 Capabilities_SupportsXmltvEpg, Capabilities_SupportsShortEpg, Capabilities_SupportsMpegTs,
                 Capabilities_SupportsHls, Capabilities_RequiresLivePathSegment,
                 BaseUrl, Username, Password)
            VALUES
                (1, 'Existing subscription', 'VLC/3.0.21', 0, '1970-01-01 00:00:00+00:00', 'xtream',
                 1, 1, 1, 1, 1, 1, 0, 1,
                 'http://panel.example:8080/', 'alice', 'rev:terc3s');

            INSERT INTO Movies
                (Id, SourceId, ExternalId, Name, HasDetail, IsWatched, SortOrder, Plot,
                 ResumePositionSeconds)
            VALUES
                (1, 1, '8412', 'Arrival', 1, 0, 0, 'Linguist meets heptapods.', 2400);
            """,
            cancellationToken);

        // Act
        await using (var context = new LtrDbContext(options, new ReversingCredentialProtector()))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        // Assert
        await using var verifyContext = new LtrDbContext(options, new ReversingCredentialProtector());

        var movie = await verifyContext.GetMovieAsync(1, cancellationToken);
        movie.ShouldNotBeNull();
        movie.Plot.ShouldBe("Linguist meets heptapods.", "a fetched synopsis is expensive to get back");
        movie.ResumePositionSeconds.ShouldBe(2400, "the position is the viewer's own data");
        movie.HasDetail.ShouldBeTrue();

        // The new column starts unset, which is what makes an existing film ask once and then settle.
        movie.DetailAttemptedUtc.ShouldBeNull();

        // And it is writable afterwards, not merely present.
        await verifyContext.RecordMovieDetailAbsentAsync(1, SixPm, cancellationToken);
        (await verifyContext.GetMovieAsync(1, cancellationToken))!.DetailAttemptedUtc.ShouldBe(SixPm);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
