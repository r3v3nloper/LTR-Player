namespace LTR.Player.Wpf;

/// <summary>
/// Keeps the settings in memory, so a test can read back what would have been written.
/// </summary>
/// <remarks>
/// The file behaviour it stands in for — a corrupt document, a missing directory, the write through a
/// temporary file — is covered against the real store and the real file system in
/// <see cref="PlayerSettingsStoreTests"/>.
/// </remarks>
internal sealed class FakePlayerSettingsStore : IPlayerSettingsStore
{
    private readonly PlayerSettings _settings;

    public FakePlayerSettingsStore(PlayerSettings settings)
    {
        _settings = settings;
    }

    /// <summary>What the last save was handed, or null when nothing has been saved.</summary>
    public PlayerSettings? Saved { get; private set; }

    public PlayerSettings Load()
    {
        return _settings;
    }

    public void Save(PlayerSettings settings)
    {
        Saved = settings;
    }
}
