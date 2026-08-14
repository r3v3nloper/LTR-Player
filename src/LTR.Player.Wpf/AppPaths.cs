using System.IO;
using LTR.Persistence;

namespace LTR.Player.Wpf;

/// <summary>
/// Locations the desktop player stores data in.
/// </summary>
/// <remarks>
/// The database location itself comes from <see cref="LtrDatabaseLocation"/>, so the player and the
/// command line tool cannot end up on different files.
/// </remarks>
internal static class AppPaths
{
    public static string DataDirectory => LtrDatabaseLocation.DataDirectory;

    public static string DatabaseFile => LtrDatabaseLocation.DatabaseFile;

    public static string LogFile => Path.Combine(DataDirectory, "logs", "ltr-player-.log");

    /// <summary>
    /// The settings file, beside the database rather than inside it.
    /// </summary>
    /// <remarks>
    /// The catalogue is a cache that gets quarantined when it cannot be read, and settings kept in it would
    /// be thrown away with it. A file is also editable by hand, which is what matters when a bad value is
    /// what stops the window opening.
    /// </remarks>
    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
}
