using LTR.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LTR.Persistence;

/// <summary>
/// Builds a context for the EF Core command line tools.
/// </summary>
/// <remarks>
/// The tooling cannot construct <see cref="LtrDbContext"/> itself, because the context takes an
/// <see cref="ICredentialProtector"/> that only the application's container knows about. The
/// connection string here is irrelevant to migration scaffolding — no database is opened — but a
/// provider must be configured so that SQLite's type mappings shape the generated schema.
/// </remarks>
internal sealed class LtrDbContextDesignTimeFactory : IDesignTimeDbContextFactory<LtrDbContext>
{
    public LtrDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LtrDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new LtrDbContext(options, new PassThroughCredentialProtector());
    }
}
