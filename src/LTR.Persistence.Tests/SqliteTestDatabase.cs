using LTR.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LTR.Persistence;

/// <summary>
/// An isolated SQLite database, held in memory for the lifetime of one test.
/// </summary>
/// <remarks>
/// Real SQLite rather than EF's in-memory provider, because the behaviour under test includes unique
/// indexes, cascade rules and <c>ExecuteUpdate</c> translation — none of which the in-memory provider
/// models. The connection is kept open deliberately: an in-memory database is discarded the moment
/// its last connection closes.
/// </remarks>
internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<LtrDbContext> _options;
    private readonly ICredentialProtector _credentialProtector;

    private SqliteTestDatabase(
        SqliteConnection connection,
        DbContextOptions<LtrDbContext> options,
        ICredentialProtector credentialProtector)
    {
        _connection = connection;
        _options = options;
        _credentialProtector = credentialProtector;
    }

    public static async Task<SqliteTestDatabase> CreateAsync(
        ICredentialProtector? credentialProtector = null,
        CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var options = new DbContextOptionsBuilder<LtrDbContext>()
            .UseSqlite(connection)
            .Options;

        var database = new SqliteTestDatabase(
            connection,
            options,
            credentialProtector ?? new ReversingCredentialProtector());

        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        return database;
    }

    /// <summary>
    /// Creates a fresh context over the same database, so a test can verify what was actually
    /// persisted rather than what is still sitting in a change tracker.
    /// </summary>
    public LtrDbContext CreateContext()
    {
        return new LtrDbContext(_options, _credentialProtector);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
    }
}
