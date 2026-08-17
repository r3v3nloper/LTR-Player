using System.IO;
using LTR.Core;
using LTR.Core.Security;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace LTR.Catalogue;

/// <summary>
/// Covers what happens at startup, including the case that used to make the application unopenable.
/// </summary>
/// <remarks>
/// <para>
/// Against a real file on disk, because that is the only way to reach the quarantine: the whole point of it
/// is a database whose bytes are wrong, and an in-memory database cannot be given wrong bytes.
/// </para>
/// <para>
/// The guard that stops a quarantine when there is no file to move has no test here. Reaching it needs
/// corruption and an absent file at once, and an absent file is created rather than corrupt — a test for it
/// could only assert that a branch it never enters was not entered.
/// </para>
/// </remarks>
public sealed class CataloguePreparationTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ltr-preparation-" + Guid.NewGuid().ToString("N"));

    public CataloguePreparationTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task PrepareCatalogueAsync_OnAFreshDatabase_MigratesItAndReportsNothingUnusual()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = BuildServices(DatabasePath);

        // Act
        var preparation = await services.PrepareCatalogueAsync(cancellationToken);

        // Assert
        preparation.WasQuarantined.ShouldBeFalse();
        preparation.UpgradedCredentials.ShouldBe(0);

        var store = services.GetRequiredService<CatalogueStore>();
        (await store.GetSourcesAsync(cancellationToken)).ShouldBeEmpty();
    }

    /// <summary>
    /// The defect this exists for: migration is the first thing either application does, so a corrupt
    /// catalogue threw before any window opened and the player could not be started at all.
    /// </summary>
    [Fact]
    public async Task PrepareCatalogueAsync_OnAnUnreadableDatabase_SetsItAsideAndStartsOver()
    {
        // Arrange: a file that is not a database. A half-written download and an interrupted write both
        // present as this.
        var cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(DatabasePath, "this is not a database", cancellationToken);

        await using var services = BuildServices(DatabasePath);

        // Act
        var preparation = await services.PrepareCatalogueAsync(cancellationToken);

        // Assert
        preparation.WasQuarantined.ShouldBeTrue();
        File.Exists(preparation.QuarantinedDatabasePath!).ShouldBeTrue("the evidence is kept, not deleted");
        (await File.ReadAllTextAsync(preparation.QuarantinedDatabasePath!, cancellationToken))
            .ShouldBe("this is not a database");

        // And the catalogue works afterwards, rather than merely not throwing.
        var store = services.GetRequiredService<CatalogueStore>();
        (await store.GetSourcesAsync(cancellationToken)).ShouldBeEmpty();
    }

    private string DatabasePath => Path.Combine(_directory, "catalogue.db");

    private static ServiceProvider BuildServices(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICredentialProtector, PassThroughCredentialProtector>();
        services.AddCatalogue($"Data Source={databasePath}");

        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
