using System.IO;

namespace LTR.Persistence;

/// <summary>
/// Where the catalogue database lives.
/// </summary>
/// <remarks>
/// Stated here rather than in each application, so the desktop player and the command line tool
/// cannot drift onto different files. Which database an instance uses is exactly the sort of question
/// that is impossible to answer once two places decide it independently.
/// </remarks>
public static class LtrDatabaseLocation
{
    private const string FolderName = "LTR-Player";

    /// <summary>
    /// Per-user data directory, created on first access.
    /// </summary>
    /// <remarks>
    /// Local application data rather than roaming: the catalogue is a cache of one machine's
    /// subscription state and can run to tens of megabytes, which has no business syncing to a domain
    /// profile.
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

    public static string ConnectionString => $"Data Source={DatabaseFile}";
}
