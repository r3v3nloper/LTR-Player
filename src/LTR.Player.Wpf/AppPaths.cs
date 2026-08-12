using System.IO;

namespace LTR.Player.Wpf;

/// <summary>
/// Locations the application stores data in.
/// </summary>
internal static class AppPaths
{
    private const string FolderName = "LTR-Player";

    /// <summary>
    /// Per-user data directory, created on first access.
    /// </summary>
    /// <remarks>
    /// Local application data rather than roaming: the catalogue is a cache of one machine's
    /// subscription state and can run to tens of megabytes, which has no business syncing to a
    /// domain profile.
    /// </remarks>
    public static string DataDirectory
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                FolderName);

            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static string DatabaseFile => Path.Combine(DataDirectory, "catalogue.db");

    public static string LogFile => Path.Combine(DataDirectory, "logs", "ltr-player-.log");
}
