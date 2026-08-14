using System.IO;
using LTR.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LTR.Catalogue;

/// <summary>
/// Registers the catalogue layer, including the database behind it.
/// </summary>
/// <remarks>
/// The database registration lives here so that applications do not have to know one exists. Both the
/// window and the command line tool previously registered the context themselves, which meant each had
/// to agree about the connection string and about credential protection.
/// </remarks>
public static class CatalogueServiceCollectionExtensions
{
    /// <param name="connectionString">
    /// Overrides where the database lives. Left unset by both applications, which is the point of the
    /// shared default; a test supplies one so that startup preparation can be exercised against a real
    /// file rather than only against the user's own.
    /// </param>
    public static IServiceCollection AddCatalogue(
        this IServiceCollection services,
        string? connectionString = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<LtrDbContext>(options =>
            options.UseSqlite(connectionString ?? LtrDatabaseLocation.ConnectionString));

        // Singletons: each creates a scope per operation rather than holding a context, so none pins a
        // unit of work open.
        services.TryAddSingleton<CatalogueUnitOfWork>();
        services.TryAddSingleton<ICatalogueStore, CatalogueStore>();
        services.TryAddSingleton<ISourceImportService, SourceImportService>();
        services.TryAddSingleton<IGuideImportService, GuideImportService>();
        services.TryAddSingleton<IVodDetailService, VodDetailService>();
        services.TryAddSingleton<IStreamFailureExplainer, StreamFailureExplainer>();

        // Singleton, and stateful unlike the rest of this layer: one stream is open at a time, so one thing
        // is being watched at a time. The same reasoning that makes the playback session a singleton.
        services.TryAddSingleton<WatchProgressRecorder>();

        return services;
    }

    /// <summary>
    /// Brings the database up to date, protects credentials that predate protection, and sets aside a
    /// database that cannot be read at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both applications share one database and either may be started first, so whichever runs must
    /// leave it in a state the other understands.
    /// </para>
    /// <para>
    /// The quarantine exists because this is the first thing either application does, and until it
    /// succeeds nothing else can run. A corrupt catalogue therefore used to be fatal at startup — and a
    /// catalogue is a cache of a subscription that can be fetched again, so starting over beats an
    /// application that will not open. It is attempted once: a second failure is not corruption of the
    /// file we just moved and must surface.
    /// </para>
    /// </remarks>
    public static async Task<CataloguePreparation> PrepareCatalogueAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        try
        {
            var upgraded = await MigrateAsync(services, cancellationToken).ConfigureAwait(false);
            return new CataloguePreparation(upgraded, QuarantinedDatabasePath: null);
        }
        catch (Exception exception) when (SqliteDatabaseFile.IsCorruption(exception))
        {
            var quarantined = QuarantineDatabase(services);

            if (quarantined is null)
            {
                // Nothing was moved, so retrying would fail the same way. Most likely the database is not
                // the file this process knows about — an in-memory one, or a path someone else supplied.
                throw;
            }

            var upgraded = await MigrateAsync(services, cancellationToken).ConfigureAwait(false);
            return new CataloguePreparation(upgraded, quarantined);
        }
    }

    private static async Task<int> MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        return await context.UpgradeStoredCredentialsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves the database this container is configured against, whichever file that is.
    /// </summary>
    /// <remarks>
    /// The path is read from the connection rather than assumed to be the shared default, so a container
    /// pointed at another file cannot cause the user's own catalogue to be moved.
    /// </remarks>
    private static string? QuarantineDatabase(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();
        var databasePath = context.Database.GetDbConnection().DataSource;

        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
        {
            return null;
        }

        var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        return SqliteDatabaseFile.Quarantine(databasePath, timeProvider.GetUtcNow());
    }
}
