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
    public static IServiceCollection AddCatalogue(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<LtrDbContext>(options =>
            options.UseSqlite(LtrDatabaseLocation.ConnectionString));

        // Singletons: each creates a scope per operation rather than holding a context, so none pins a
        // unit of work open.
        services.TryAddSingleton<CatalogueUnitOfWork>();
        services.TryAddSingleton<ICatalogueStore, CatalogueStore>();
        services.TryAddSingleton<ISourceImportService, SourceImportService>();
        services.TryAddSingleton<IGuideImportService, GuideImportService>();

        return services;
    }

    /// <summary>
    /// Brings the database up to date and protects credentials that predate protection.
    /// </summary>
    /// <remarks>
    /// Both applications share one database and either may be started first, so whichever runs must
    /// leave it in a state the other understands. Returning the number upgraded lets the caller report
    /// it in whatever way suits it.
    /// </remarks>
    public static async Task<int> PrepareCatalogueAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();

        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        return await context.UpgradeStoredCredentialsAsync(cancellationToken).ConfigureAwait(false);
    }
}
