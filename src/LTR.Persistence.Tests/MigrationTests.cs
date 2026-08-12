using LTR.Core.Sources;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LTR.Persistence;

/// <summary>
/// Verifies the checked-in migrations, rather than the model, produce a working database.
/// </summary>
/// <remarks>
/// The other tests here build their schema with <c>EnsureCreated</c>, which reads the model directly
/// and therefore cannot notice a migration that has drifted from it. Only the migrations ship, so a
/// mismatch would surface on a user's machine and nowhere else.
/// </remarks>
public sealed class MigrationTests
{
    [Fact]
    public async Task Migrations_BuildADatabaseTheApplicationCanUse()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(cancellationToken);

        var options = new DbContextOptionsBuilder<LtrDbContext>()
            .UseSqlite(connection)
            .Options;

        // Act
        await using var context = new LtrDbContext(options, new ReversingCredentialProtector());
        await context.Database.MigrateAsync(cancellationToken);

        // Assert: a round trip through the migrated schema, exercising the discriminator, the owned
        // capabilities and the unique index at once.
        var sourceId = await context.AddSourceAsync(
            new XtreamSource
            {
                Name = "Migrated source",
                BaseUrl = new Uri("http://panel.example:8080", UriKind.Absolute),
                Username = "alice",
                Password = "s3cret",
                CreatedUtc = DateTimeOffset.UnixEpoch,
                Capabilities = new ProviderCapabilities { SupportsLive = true },
            },
            cancellationToken);

        sourceId.ShouldBeGreaterThan(0);

        var pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        pendingMigrations.ShouldBeEmpty("the model must not have drifted from the migrations");
    }
}
