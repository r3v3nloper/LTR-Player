using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LTR.Persistence;

/// <summary>
/// Recognises an unreadable SQLite database and sets it aside.
/// </summary>
/// <remarks>
/// <para>
/// A corrupt catalogue used to take the whole application down at startup, before any window opened,
/// with a raw SQLite message and no way forward: migrating is the first thing that happens and it cannot
/// succeed. Since the catalogue is a cache of a subscription that can be fetched again, starting over is a
/// far better outcome than an application that will not start.
/// </para>
/// <para>
/// Set aside rather than deleted. What corrupted it is worth knowing, and a file the user can send on is
/// the only evidence there will ever be — deleting it to make the error go away is how the cause stays
/// unknown.
/// </para>
/// </remarks>
public static class SqliteDatabaseFile
{
    /// <summary>SQLITE_CORRUPT — the file is a database, and its pages do not make sense.</summary>
    private const int Corrupt = 11;

    /// <summary>SQLITE_NOTADB — the file is not a database at all, which a truncated one looks like.</summary>
    private const int NotADatabase = 26;

    /// <summary>
    /// The companion files SQLite keeps beside a database. They belong to it and are meaningless without
    /// it, so they go wherever it goes.
    /// </summary>
    private static readonly string[] CompanionSuffixes = ["-wal", "-shm"];

    /// <summary>
    /// Whether <paramref name="exception"/> means the database cannot be read at all, as opposed to any of
    /// the ordinary reasons a query fails.
    /// </summary>
    /// <remarks>
    /// Inner exceptions are searched because EF wraps: the same corruption arrives bare from a migration
    /// and inside a <c>DbUpdateException</c> from a save.
    /// </remarks>
    public static bool IsCorruption(Exception? exception)
    {
        for (var candidate = exception; candidate is not null; candidate = candidate.InnerException)
        {
            if (candidate is SqliteException sqlite && sqlite.SqliteErrorCode is Corrupt or NotADatabase)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Renames a database and its companion files out of the way, and reports where they went.
    /// </summary>
    /// <param name="databasePath">The database to set aside. Need not exist.</param>
    /// <param name="at">Timestamp for the new name, so successive quarantines do not collide.</param>
    /// <returns>
    /// The path the database was moved to, or <see langword="null"/> when there was nothing to move.
    /// </returns>
    public static string? Quarantine(string databasePath, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        if (!File.Exists(databasePath))
        {
            return null;
        }

        // The failed attempt returned its connection to the pool rather than closing it, and Windows
        // refuses to rename an open file. Without this the quarantine fails with an access violation and
        // the corruption resurfaces as something unrelated.
        SqliteConnection.ClearAllPools();

        var target = ReserveQuarantinePath(databasePath, at);

        // The main file last would leave a moment where the companions are gone and the database is not,
        // which is a state SQLite would try to recover from. This way the database goes first.
        File.Move(databasePath, target);

        foreach (var suffix in CompanionSuffixes)
        {
            var companion = databasePath + suffix;

            if (File.Exists(companion))
            {
                File.Move(companion, target + suffix);
            }
        }

        return target;
    }

    /// <summary>
    /// Finds a name not already taken, so quarantining twice in the same second keeps both files.
    /// </summary>
    private static string ReserveQuarantinePath(string databasePath, DateTimeOffset at)
    {
        var stamp = at.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var candidate = $"{databasePath}.corrupt-{stamp}";
        var attempt = 2;

        while (File.Exists(candidate))
        {
            candidate = $"{databasePath}.corrupt-{stamp}-{attempt}";
            attempt++;
        }

        return candidate;
    }
}
