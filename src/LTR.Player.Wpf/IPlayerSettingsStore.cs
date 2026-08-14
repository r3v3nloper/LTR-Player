namespace LTR.Player.Wpf;

/// <summary>
/// Where the settings are kept.
/// </summary>
/// <remarks>
/// An interface for one implementation, which is worth it here for one reason: the alternative is a test that
/// either writes the developer's own settings file or asserts against a temporary directory it then has to
/// clean up. The file behaviour itself — corrupt documents, a missing directory, the atomic write — is tested
/// against the real store and the real file system.
/// </remarks>
public interface IPlayerSettingsStore
{
    /// <summary>The stored settings, or the defaults when there are none or they cannot be read.</summary>
    PlayerSettings Load();

    /// <summary>Stores the settings, reporting in the log rather than throwing if it could not.</summary>
    void Save(PlayerSettings settings);
}
