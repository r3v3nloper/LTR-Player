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
}
