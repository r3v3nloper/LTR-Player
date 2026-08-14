using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Catalogue;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// The settings pane: engine tuning, and the two things about a source that cannot be probed.
/// </summary>
/// <remarks>
/// <para>
/// Edits a copy and writes it on save, rather than binding straight at the live settings. Playback tuning is
/// the kind of value a viewer arrives at by trying figures, and a slider that took effect per keystroke would
/// have written six settings files on the way to one.
/// </para>
/// <para>
/// Owns whether it is open, which is what keeps the shell from growing another flag: the sections bind their
/// visibility to <see cref="IsOpen"/> and the shell only has to hand over the selected source.
/// </para>
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>
    /// Narrowest and widest buffer that can be asked for.
    /// </summary>
    /// <remarks>
    /// Clamped rather than validated, because a text box takes any number and both failure modes are silent:
    /// no buffer at all becomes a stream that stutters continuously, and thirty seconds of one looks exactly
    /// like a channel that will not start.
    /// </remarks>
    internal const int MinimumCaching = 100;

    internal const int MaximumCaching = 10_000;

    private readonly IPlayerSettingsStore _store;
    private readonly ICatalogueStore _catalogue;
    private readonly StatusLine _status;
    private readonly ILogger<SettingsViewModel> _logger;

    /// <summary>The source whose own settings are on screen, or null when none is selected.</summary>
    private PlaylistSource? _source;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private int _liveNetworkCachingMilliseconds;

    [ObservableProperty]
    private int _networkCachingMilliseconds;

    [ObservableProperty]
    private HardwareDecodingChoice _hardwareDecoding = HardwareDecodingChoice.For(default);

    [ObservableProperty]
    private string _userAgent = string.Empty;

    [ObservableProperty]
    private StreamFormatChoice _preferredStreamFormat = StreamFormatChoice.For(default);

    /// <summary>The name of the source being edited, or empty when there is none.</summary>
    [ObservableProperty]
    private string _sourceName = string.Empty;

    /// <param name="settings">
    /// The one instance for the process, loaded before the container was built because the engine's options
    /// come from it. Injected rather than loaded here: a second copy would mean the volume the overlay writes
    /// and the tuning this pane writes ending up in different objects, one of which is never saved.
    /// </param>
    public SettingsViewModel(
        IPlayerSettingsStore store,
        PlayerSettings settings,
        ICatalogueStore catalogue,
        StatusLine status,
        ILogger<SettingsViewModel> logger)
    {
        _store = store;
        _catalogue = catalogue;
        _status = status;
        _logger = logger;

        Settings = settings;
    }

    /// <summary>
    /// The live settings, shared with whatever else reads them.
    /// </summary>
    /// <remarks>
    /// One instance for the process: the overlay writes the viewer's volume into it as they change it, and
    /// this pane writes the tuning. Both are persisted by the same <see cref="Persist"/> on the way out,
    /// so neither has to know about the other.
    /// </remarks>
    public PlayerSettings Settings { get; }

    /// <summary>Whether a source is selected, and therefore whether its own settings can be edited.</summary>
    public bool HasSource => _source is not null;

    /// <summary>
    /// Shows the pane, filled from what is stored.
    /// </summary>
    /// <remarks>
    /// The source is handed over rather than looked up, for the same reason the timeline is handed the visible
    /// channels: only the shell knows which is selected, and the sections do not reference one another.
    /// </remarks>
    public void Open(PlaylistSource? source)
    {
        _source = source;

        SourceName = source?.Name ?? string.Empty;
        UserAgent = source?.UserAgent ?? PlaylistSource.DefaultUserAgent;
        PreferredStreamFormat = StreamFormatChoice.For(
            source?.PreferredStreamFormat ?? default);

        LiveNetworkCachingMilliseconds = Settings.Playback.LiveNetworkCachingMilliseconds;
        NetworkCachingMilliseconds = Settings.Playback.NetworkCachingMilliseconds;
        HardwareDecoding = HardwareDecodingChoice.For(Settings.Playback.HardwareDecoding);

        OnPropertyChanged(nameof(HasSource));
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
    }

    /// <summary>
    /// Writes the settings file, taking whatever the rest of the player has put in it.
    /// </summary>
    /// <remarks>
    /// Called on the way out of the window as well as from the save button, because the volume the viewer
    /// left the player at is written by the overlay into the same object and never passes through this pane.
    /// </remarks>
    public void Persist()
    {
        _store.Save(Settings);
    }

    /// <summary>
    /// Stores everything and says what will not take effect until the player is restarted.
    /// </summary>
    /// <remarks>
    /// Both figures reach LibVLC as startup arguments or as options read when the engine is constructed, so
    /// there is no honest way to apply them to the engine already running. Saying so is better than appearing
    /// to apply them and leaving the viewer to conclude the setting does nothing.
    /// </remarks>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        Settings.Playback.LiveNetworkCachingMilliseconds = Math.Clamp(
            LiveNetworkCachingMilliseconds,
            MinimumCaching,
            MaximumCaching);

        Settings.Playback.NetworkCachingMilliseconds = Math.Clamp(
            NetworkCachingMilliseconds,
            MinimumCaching,
            MaximumCaching);

        Settings.Playback.HardwareDecoding = HardwareDecoding.Value;

        Persist();

        await SaveSourceSettingsAsync(cancellationToken).ConfigureAwait(true);

        _status.Text = "Settings saved. The buffer and decoding figures apply when the player is restarted.";
        Close();
    }

    private async Task SaveSourceSettingsAsync(CancellationToken cancellationToken)
    {
        if (_source is not { } source)
        {
            return;
        }

        var agent = string.IsNullOrWhiteSpace(UserAgent)
            ? PlaylistSource.DefaultUserAgent
            : UserAgent.Trim();

        try
        {
            await _catalogue
                .UpdateSourceSettingsAsync(source.Id, agent, PreferredStreamFormat.Value, cancellationToken)
                .ConfigureAwait(true);

            // The in-memory source is what resolves the next stream's address, so it has to agree with what
            // was just stored or the change appears to need a restart when it does not.
            source.UserAgent = agent;
            source.PreferredStreamFormat = PreferredStreamFormat.Value;
        }
        catch (OperationCanceledException)
        {
            // The window is closing.
        }
        catch (Exception exception)
        {
            PlayerLog.SourceSettingsNotSaved(_logger, exception, source.Name);
            _status.Text = $"The settings for {source.Name} could not be stored. Details are in the log.";
        }
    }
}
