using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LTR.Persistence;

/// <summary>
/// Covers setting an unreadable database aside.
/// </summary>
/// <remarks>
/// Against real files, because every risk here is a file-system one: moving the wrong companion, clobbering
/// an earlier quarantine, or leaving a database behind without the write-ahead log that belongs to it.
/// </remarks>
public sealed class SqliteDatabaseFileTests : IDisposable
{
    private static readonly DateTimeOffset SixPm = new(2026, 8, 12, 18, 0, 0, TimeSpan.Zero);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "ltr-quarantine-" + Guid.NewGuid().ToString("N"));

    public SqliteDatabaseFileTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void Quarantine_MovesTheDatabaseAndTheFilesThatBelongToIt()
    {
        // Arrange
        var databasePath = Path.Combine(_directory, "catalogue.db");
        File.WriteAllText(databasePath, "not a database");
        File.WriteAllText(databasePath + "-wal", "write ahead log");
        File.WriteAllText(databasePath + "-shm", "shared memory");

        // Act
        var quarantined = SqliteDatabaseFile.Quarantine(databasePath, SixPm);

        // Assert
        quarantined.ShouldNotBeNull();
        quarantined.ShouldEndWith("catalogue.db.corrupt-20260812-180000");

        File.Exists(databasePath).ShouldBeFalse("the path has to be free for a fresh database");
        File.Exists(databasePath + "-wal").ShouldBeFalse();
        File.Exists(databasePath + "-shm").ShouldBeFalse();

        // Kept, not deleted: what corrupted it is worth knowing, and this is the only evidence there is.
        File.ReadAllText(quarantined).ShouldBe("not a database");
        File.ReadAllText(quarantined + "-wal").ShouldBe("write ahead log");
        File.ReadAllText(quarantined + "-shm").ShouldBe("shared memory");
    }

    [Fact]
    public void Quarantine_WithNoCompanionFiles_MovesWhatIsThere()
    {
        // Arrange
        var databasePath = Path.Combine(_directory, "catalogue.db");
        File.WriteAllText(databasePath, "not a database");

        // Act
        var quarantined = SqliteDatabaseFile.Quarantine(databasePath, SixPm);

        // Assert
        quarantined.ShouldNotBeNull();
        File.Exists(databasePath).ShouldBeFalse();
    }

    [Fact]
    public void Quarantine_WhenThereIsNoDatabase_DoesNothingAndSaysSo()
    {
        // Arrange, Act
        var quarantined = SqliteDatabaseFile.Quarantine(Path.Combine(_directory, "absent.db"), SixPm);

        // Assert: the caller uses this to decide whether retrying is worth anything.
        quarantined.ShouldBeNull();
    }

    /// <summary>
    /// Two quarantines within the same second must both survive, or the second silently destroys the
    /// evidence from the first.
    /// </summary>
    [Fact]
    public void Quarantine_TwiceAtTheSameInstant_KeepsBoth()
    {
        // Arrange
        var databasePath = Path.Combine(_directory, "catalogue.db");
        File.WriteAllText(databasePath, "first");

        var first = SqliteDatabaseFile.Quarantine(databasePath, SixPm);
        File.WriteAllText(databasePath, "second");

        // Act
        var second = SqliteDatabaseFile.Quarantine(databasePath, SixPm);

        // Assert
        second.ShouldNotBe(first);
        File.ReadAllText(first!).ShouldBe("first");
        File.ReadAllText(second!).ShouldBe("second");
    }

    /// <summary>
    /// The pooled connection from the failed attempt is still open, and Windows will not rename an open
    /// file — so the quarantine has to close it first.
    /// </summary>
    [Fact]
    public async Task Quarantine_SucceedsWhileAConnectionToItIsStillPooled()
    {
        // Arrange: a real database, opened and returned to the pool.
        var cancellationToken = TestContext.Current.CancellationToken;
        var databasePath = Path.Combine(_directory, "catalogue.db");

        await using (var context = new LtrDbContext(
            new DbContextOptionsBuilder<LtrDbContext>().UseSqlite($"Data Source={databasePath}").Options,
            new ReversingCredentialProtector()))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        // Act
        var quarantined = SqliteDatabaseFile.Quarantine(databasePath, SixPm);

        // Assert
        quarantined.ShouldNotBeNull();
        File.Exists(databasePath).ShouldBeFalse();
    }

    [Fact]
    public void IsCorruption_RecognisesAnUnreadableDatabase()
    {
        // Arrange: the codes SQLite reports for a damaged file and for one that is not a database at all.
        // Act & Assert
        SqliteDatabaseFile.IsCorruption(new SqliteException("malformed", 11, 11)).ShouldBeTrue();
        SqliteDatabaseFile.IsCorruption(new SqliteException("not a database", 26, 26)).ShouldBeTrue();
    }

    /// <summary>
    /// EF wraps: the same corruption arrives bare from a migration and nested from a save.
    /// </summary>
    [Fact]
    public void IsCorruption_LooksThroughWrappingExceptions()
    {
        // Arrange
        var wrapped = new InvalidOperationException(
            "An error occurred while saving.",
            new SqliteException("malformed", 11, 11));

        // Act & Assert
        SqliteDatabaseFile.IsCorruption(wrapped).ShouldBeTrue();
    }

    /// <summary>
    /// A unique index violation or a locked database must never move the user's catalogue aside.
    /// </summary>
    [Fact]
    public void IsCorruption_RejectsTheOrdinaryReasonsAQueryFails()
    {
        // Arrange & Act & Assert
        SqliteDatabaseFile.IsCorruption(new SqliteException("constraint failed", 19, 2067)).ShouldBeFalse();
        SqliteDatabaseFile.IsCorruption(new SqliteException("database is locked", 5, 5)).ShouldBeFalse();
        SqliteDatabaseFile.IsCorruption(new InvalidOperationException("something else")).ShouldBeFalse();
        SqliteDatabaseFile.IsCorruption(exception: null).ShouldBeFalse();
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
