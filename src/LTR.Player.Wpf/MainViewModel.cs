using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Drives the main window: composes the catalogue sections and the guide, and turns what the viewer does
/// into work for the coordinators.
/// </summary>
/// <remarks>
/// <para>
/// It is also the sections' <see cref="ISourceCoordinator"/>, which is what keeps them from knowing about one
/// another. Only this class can reach the lists, the guide and playback at once, and those are exactly the
/// things source management has to trigger.
/// </para>
/// <para>
/// What it does *not* do is now the shorter list. Opening a stream and remembering a position belong to
/// <see cref="PlaybackCoordinator"/>, deciding which item to play to <see cref="PlaybackCommands"/>, running a
/// guide import to <see cref="GuideImportCoordinator"/>. What is left is composition, the section selection and
/// its availability rules, the guide, the settings pane, the window's lifetime, and the keystroke dispatch.
/// </para>
/// <para>
/// It regrew past its own size three times, always the same way: being the only class that can reach
/// everything, so anything needing two of them lands here. Expect a fourth. The question to ask of a new
/// method is not how long this file is but whether it needs the *window* — a section, the panes and the
/// lifetime token at once — or only a section and playback, which is <see cref="PlaybackCommands"/>'.
/// </para>
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, ISourceCoordinator, IAsyncDisposable
{
    private readonly PlaybackCoordinator _playback;
    private readonly GuideImportCoordinator _guideImport;
    private readonly PlayerActions _playerActions;
    private readonly ILogger<MainViewModel> _logger;

    /// <summary>
    /// The notifications this class carries from the objects that own a property to the commands and
    /// properties here that depend on it. Registered in <see cref="RegisterNotificationForwards"/>.
    /// </summary>
    private readonly CrossObjectNotifications _notifications;

    /// <summary>
    /// Cancelled when the window closes, and linked into everything the shell starts.
    /// </summary>
    /// <remarks>
    /// Two things need it. A guide import runs for minutes and writes to the database throughout: left
    /// running past shutdown it would write into a disposed container, and the process would not exit while
    /// it did. And loading a catalogue of seventeen thousand channels takes long enough that a user who
    /// closes the window mid-load should not be made to wait for it.
    /// </remarks>
    private readonly CancellationTokenSource _shellLifetime = new();

    /// <summary>
    /// The list reloads and detail fetches started in answer to a property change.
    /// </summary>
    /// <remarks>
    /// Followed rather than kept: nothing in the application waits on them, but something has to be able to
    /// ask whether the shell has finished reacting — see <see cref="SectionWorkCompletion"/>.
    /// </remarks>
    private readonly PendingWork _sectionWork = new();

    [ObservableProperty]
    private CatalogueSection _selectedSection = CatalogueSection.Live;

    public MainViewModel(
        SourceManagementViewModel sources,
        ChannelListViewModel channels,
        GuideViewModel guide,
        MovieListViewModel movies,
        SeriesCatalogueViewModel series,
        ContinueWatchingViewModel continueWatching,
        StatusLine status,
        PlaybackCoordinator playback,
        PlayerOverlayViewModel playerOverlay,
        SettingsViewModel settings,
        GuideImportCoordinator guideImport,
        PlaybackCommands playbackCommands,
        ILogger<MainViewModel> logger)
    {
        SourceManagement = sources;
        Channels = channels;
        Guide = guide;
        Movies = movies;
        SeriesCatalogue = series;
        ContinueWatching = continueWatching;
        Status = status;
        PlayerOverlay = playerOverlay;
        Settings = settings;
        PlaybackCommands = playbackCommands;

        _playback = playback;
        _guideImport = guideImport;
        _logger = logger;

        // The section selection is this class's, so the zap that needs it asks for it rather than holding it.
        PlaybackCommands.ShowChannelList = () => SelectedSection = CatalogueSection.Live;

        _notifications = new CrossObjectNotifications(OnPropertyChanged);
        RegisterNotificationForwards();

        // After the forwards, deliberately: a section raises one event and every subscriber runs in
        // subscription order, so a command whose guard the work below may change is notified first. That order
        // now depends on construction order too — PlaybackCommands registers its own forwards in its
        // constructor, which the container runs before this one, so its guards are notified before these
        // handlers start anything. Constructing it by hand *after* this point would silently reverse that.
        Movies.PropertyChanged += OnMovieListChanged;
        SeriesCatalogue.PropertyChanged += OnSeriesChanged;
        _playback.PropertyChanged += OnPlaybackChanged;

        SourceManagement.Coordinator = this;

        // Built here rather than injected, because the operations it needs are this class's own and the
        // commands' are PlaybackCommands'.
        _playerActions = new PlayerActions(
            PlayerOverlay,
            PlaybackCommands.StopAsync,
            PlaybackCommands.PlayAdjacentAsync,
            ToggleGuideAsync);
    }

    public SourceManagementViewModel SourceManagement { get; }

    public ChannelListViewModel Channels { get; }

    public GuideViewModel Guide { get; }

    public MovieListViewModel Movies { get; }

    public SeriesCatalogueViewModel SeriesCatalogue { get; }

    public ContinueWatchingViewModel ContinueWatching { get; }

    public StatusLine Status { get; }

    /// <summary>The controls drawn over the picture.</summary>
    public PlayerOverlayViewModel PlayerOverlay { get; }

    /// <summary>The settings pane, which owns whether it is open.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>
    /// What the viewer can ask to be played, which the markup binds through this rather than through here.
    /// </summary>
    /// <remarks>
    /// Exposed rather than forwarded command by command: ten pass-through properties would be ten more places
    /// to forget a notification, which is the defect class this window has shipped three times.
    /// </remarks>
    public PlaybackCommands PlaybackCommands { get; }

    /// <summary>
    /// Whether the catalogue is what the left pane is showing, rather than a form.
    /// </summary>
    /// <remarks>
    /// One positive property instead of the two negated bindings the markup had, now that a second pane can
    /// take the same space. The source picker hides with the rest deliberately: switching source behind an
    /// open settings pane would leave it editing the one that is no longer selected.
    /// </remarks>
    public bool IsShowingCatalogue => !SourceManagement.IsAddingSource && !Settings.IsOpen;

    /// <summary>What is playing, for the overlay over the video.</summary>
    public string NowPlaying => _playback.NowPlaying;

    /// <summary>The guide import in flight, or an already completed task.</summary>
    public Task GuideImportCompletion => _guideImport.Completion;

    public bool IsImportingGuide => _guideImport.IsImporting;

    /// <summary>
    /// Completes once the shell has finished reacting to the last selection or search.
    /// </summary>
    /// <remarks>
    /// Exposed for tests, which otherwise have no way to tell a section that is still loading from one that
    /// has loaded nothing. Awaiting it is not part of using the window: a viewer changing the search does not
    /// wait for the previous one, and neither does anything in the application.
    /// </remarks>
    public Task SectionWorkCompletion => _sectionWork.Completion;

    /// <summary>
    /// Loads the configured sources, so a restart lands straight in the channel list.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var lifetime = LinkedToShellLifetime(cancellationToken);
        await SourceManagement.InitializeAsync(lifetime.Token).ConfigureAwait(true);
    }

    /// <summary>
    /// Rereads what is on now, and moves the timeline's marker.
    /// </summary>
    /// <remarks>
    /// Driven by a timer the window owns. "Now" moves without anything happening in the application, so a
    /// row left alone keeps showing a programme that finished half an hour ago.
    /// </remarks>
    public async Task RefreshGuideDisplayAsync()
    {
        Guide.UpdateNowMarker();

        // Nothing is matched, so there is nothing for a reread to change — and the query behind it is the
        // largest one the player makes. A subscription with no guide imported would otherwise pay for it
        // every minute for as long as the window is open.
        //
        // Only the timer skips. The catalogue load and the post-import reload call the channel list directly,
        // which is what lets a guide that has just arrived be picked up at all.
        if (!Channels.HasGuide)
        {
            return;
        }

        try
        {
            await Channels.RefreshGuideAsync(_shellLifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The window is closing. Raised from a timer tick, so an unhandled one would crash the process
            // on the way out.
        }
        catch (Exception exception)
        {
            // A failed periodic refresh must not put a dialog in front of someone watching television.
            PlayerLog.GuideRefreshFailed(_logger, exception);
        }
    }

    /// <summary>
    /// Rereads where playback has reached, for the resume recorder and for the on-screen controls.
    /// </summary>
    /// <remarks>
    /// Driven by a timer the window owns, which runs faster while the controls are visible. One tick serves
    /// both: the recorder needs a sample every few seconds whatever is on screen, and the controls need one
    /// several times a second while they are.
    /// </remarks>
    public async Task SamplePlaybackAsync()
    {
        try
        {
            await _playback.SampleAsync(_shellLifetime.Token).ConfigureAwait(true);

            // Inside the guard with the rest, not after it. This rebuilds the track menus, and the caller is
            // an async void timer tick — so anything escaping here reaches the dispatcher's unhandled
            // handler as a dialog rather than the log, twice a second.
            PlayerOverlay.Sample();
        }
        catch (OperationCanceledException)
        {
            // The window is closing. Raised from a timer tick, so an unhandled one would take the process
            // down on the way out.
        }
        catch (Exception exception)
        {
            // Closing off a stream that ended writes to the database and rereads three lists, and a failure
            // in any of that must not put a dialog in front of someone watching television.
            PlayerLog.PlaybackSampleFailed(_logger, exception);
        }
    }

    /// <summary>
    /// Carries out what a keystroke asked for.
    /// </summary>
    /// <remarks>
    /// The window resolves a key to a <see cref="PlayerAction"/> and hands it here, so that what each action
    /// does is stated once, in a place a test can reach, rather than in a switch inside a key handler. The
    /// statement itself is <see cref="PlayerActions"/>; only the four actions that need the shell come back
    /// here.
    /// </remarks>
    public Task PerformAsync(PlayerAction action, CancellationToken cancellationToken)
    {
        return _playerActions.PerformAsync(action, cancellationToken);
    }

    /// <summary>
    /// Hands the provider connection back before the window goes away.
    /// </summary>
    /// <remarks>
    /// Not a command, because it is not a user action. Everything the shell has in flight is abandoned first,
    /// so closing the window does not wait on a catalogue load or a guide download — but the release itself
    /// is not cancellable, because a subscription permitting a single connection is unusable for minutes if
    /// the player exits still holding one.
    /// </remarks>
    public async Task ShutdownAsync()
    {
        await _shellLifetime.CancelAsync().ConfigureAwait(true);
        await _playback.ShutdownAsync().ConfigureAwait(true);

        // Last, and after the release rather than before it: the volume the viewer left the player at is
        // worth keeping, but not at the cost of delaying the one thing that has to happen on the way out.
        Settings.Persist();
    }

    async Task ISourceCoordinator.ShowCatalogueAsync(PlaylistSource? source, CancellationToken cancellationToken)
    {
        // Linked, rather than taken as given. The caller is often a property setter that has no token to
        // offer, and loading seventeen thousand channels is the longest thing the shell does — a user
        // closing the window mid-load must not be made to wait for it.
        using var lifetime = LinkedToShellLifetime(cancellationToken);

        try
        {
            await Channels.ShowAsync(source, lifetime.Token).ConfigureAwait(true);
            Guide.Attach(source, Channels.VisibleChannels);

            await Movies.ShowAsync(source, lifetime.Token).ConfigureAwait(true);
            await SeriesCatalogue.ShowAsync(source, lifetime.Token).ConfigureAwait(true);
            await ContinueWatching.ShowAsync(source, lifetime.Token).ConfigureAwait(true);

            // A section that the new source does not offer must not stay on screen showing the last one's
            // catalogue.
            if (!IsSectionAvailable(SelectedSection))
            {
                SelectedSection = CatalogueSection.Live;
            }
        }
        catch (OperationCanceledException)
        {
            // Swallowed rather than rethrown, and that matters: source management starts this without
            // awaiting it when the selection changes, so anything escaping here becomes an unobserved task
            // exception. It only became reachable once the shell gained a lifetime token to cancel.
        }
        catch (Exception exception)
        {
            PlayerLog.CatalogueLoadFailed(_logger, exception, source?.Name ?? string.Empty);
            Status.Text = $"The stored catalogue for {source?.Name} could not be read.";
        }
    }

    /// <remarks>
    /// The stop also forgets what was playing, in the coordinator: the episode belonged to the source being
    /// deleted, and next would otherwise look up its successor and open it against whichever source is selected
    /// next. Switching between configured sources does not come through here and does not stop playback, which
    /// is why <see cref="SeriesCatalogueViewModel.FindAdjacentEpisodeAsync"/> also scopes its answer to the
    /// selected source.
    /// </remarks>
    Task ISourceCoordinator.ReleasePlaybackAsync(CancellationToken cancellationToken)
    {
        return _playback.StopAsync(cancellationToken);
    }

    void ISourceCoordinator.CatalogueImported(PlaylistSource source)
    {
        StartGuideImport(source, onlyWhenStale: true);
    }

    /// <summary>
    /// Opens or closes the timeline, loading the window on the way in.
    /// </summary>
    /// <remarks>
    /// The channels are handed over here rather than when the catalogue loads, so the timeline shows what
    /// the list currently shows — a category or a search having narrowed it is exactly the filter the user
    /// wants the guide to respect.
    /// </remarks>
    [RelayCommand]
    private async Task ToggleGuideAsync(CancellationToken cancellationToken)
    {
        if (Guide.IsVisible)
        {
            Guide.Hide();
            return;
        }

        await Guide
            .ShowAsync(SourceManagement.SelectedSource, Channels.VisibleChannels, cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Opens or closes the settings pane, handing it the selected source on the way in.
    /// </summary>
    /// <remarks>
    /// The source is passed rather than looked up, for the same reason the timeline is handed the visible
    /// channels: only this class knows which source is selected, and the panes do not reference each other.
    /// </remarks>
    [RelayCommand]
    private void ToggleSettings()
    {
        if (Settings.IsOpen)
        {
            Settings.Close();
            return;
        }

        Settings.Open(SourceManagement.SelectedSource);
    }

    /// <summary>
    /// Fetches the selected source's guide on request, whether or not the stored one is still fresh.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImportGuide))]
    private void ImportGuide()
    {
        if (SourceManagement.SelectedSource is { } source)
        {
            StartGuideImport(source, onlyWhenStale: false);
        }
    }

    private bool CanImportGuide()
    {
        return !_guideImport.IsImporting && SourceManagement.SelectedSource is not null;
    }

    private void StartGuideImport(PlaylistSource source, bool onlyWhenStale)
    {
        _guideImport.Start(source, onlyWhenStale, ReloadAfterGuideImportAsync, _shellLifetime.Token);
    }

    private async Task ReloadAfterGuideImportAsync()
    {
        await Channels.RefreshGuideAsync(_shellLifetime.Token).ConfigureAwait(true);

        if (Guide.IsVisible)
        {
            await Guide.LoadAsync(_shellLifetime.Token).ConfigureAwait(true);
        }
    }

    private bool IsSectionAvailable(CatalogueSection section)
    {
        return section switch
        {
            CatalogueSection.Movies => Movies.IsAvailable,
            CatalogueSection.Series => SeriesCatalogue.IsAvailable,
            _ => true,
        };
    }

    /// <summary>
    /// Combines a caller's token with the shell's, so anything the shell starts ends when the window does.
    /// </summary>
    private CancellationTokenSource LinkedToShellLifetime(CancellationToken cancellationToken)
    {
        return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shellLifetime.Token);
    }

    /// <summary>
    /// States every notification this class has to carry across an object boundary by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>[NotifyCanExecuteChangedFor]</c> cannot cross one: the command is here and the property its guard
    /// reads belongs to a section. Without the forward the button keeps whatever state it had when the window
    /// opened — the defect class that shipped three times, and the reason the tests assert the notification
    /// rather than <c>CanExecute</c>.
    /// </para>
    /// <para>
    /// A table rather than a handler each, so that the set is readable as a set and the rule about an empty
    /// property name lives in one place. What is deliberately *not* here is anything that reacts: starting
    /// work and revealing the overlay are behaviour, and they stay below.
    /// </para>
    /// </remarks>
    private void RegisterNotificationForwards()
    {
        _notifications.When(SourceManagement, nameof(SourceManagementViewModel.SelectedSource))
            .Notifies(ImportGuideCommand);

        // Both panes take the left-hand side over, and IsShowingCatalogue is computed here from both.
        _notifications
            .When(SourceManagement, nameof(SourceManagementViewModel.IsAddingSource))
            .Raises(nameof(IsShowingCatalogue));

        _notifications.When(Settings, nameof(SettingsViewModel.IsOpen)).Raises(nameof(IsShowingCatalogue));

        _notifications
            .When(_guideImport, nameof(GuideImportCoordinator.IsImporting))
            .Raises(nameof(IsImportingGuide))
            .Notifies(ImportGuideCommand);

        // The overlay binds NowPlaying here rather than reaching into the coordinator.
        _notifications.When(_playback, nameof(PlaybackCoordinator.NowPlaying)).Raises(nameof(NowPlaying));
    }

    /// <summary>
    /// Loads a film's detail, and reruns the search, when the section reports one of them is due.
    /// </summary>
    /// <remarks>
    /// Selecting a film means a network call, which a property setter cannot await — so the section reports
    /// the selection and the shell, which owns the lifetime token, drives the work. The notifications the same
    /// changes carry are in the table above.
    /// </remarks>
    private void OnMovieListChanged(object? sender, PropertyChangedEventArgs e)
    {
        // An empty name means every property, which WPF and the toolkit both use to mean "re-read all".
        switch (e.PropertyName)
        {
            case null or "" or nameof(MovieListViewModel.SelectedMovie):
                Run(Movies.LoadSelectedDetailAsync);
                break;

            case nameof(MovieListViewModel.SearchText) or nameof(MovieListViewModel.SelectedCategory):
                Run(Movies.SearchAsync);
                break;

            default:
                break;
        }
    }

    private void OnSeriesChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null or "" or nameof(SeriesCatalogueViewModel.SelectedSeries):
                Run(SeriesCatalogue.LoadSelectedAsync);
                break;

            case nameof(SeriesCatalogueViewModel.SearchText)
                or nameof(SeriesCatalogueViewModel.SelectedCategory):
                Run(SeriesCatalogue.SearchAsync);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// Shows the controls when something new starts playing.
    /// </summary>
    /// <remarks>
    /// A reaction and not a notification, which is why it did not move into the table: the controls take
    /// themselves away again after a few seconds, and a channel change that announced nothing would leave the
    /// viewer to recognise the channel from the picture.
    /// </remarks>
    private void OnPlaybackChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or "" or nameof(PlaybackCoordinator.NowPlaying)))
        {
            return;
        }

        if (!string.IsNullOrEmpty(_playback.NowPlaying))
        {
            PlayerOverlay.Reveal();
        }
    }

    /// <summary>
    /// Runs work triggered by a property change, which cannot be awaited where it is raised.
    /// </summary>
    /// <remarks>
    /// Each of these reloads a list, is cancelled by the shell lifetime and handles its own failures, so
    /// nothing in the application waits on one. It is followed all the same, through
    /// <see cref="SectionWorkCompletion"/>: a test otherwise has no way to know the shell has finished
    /// reacting, and the version of this that had none made the tests spin on <c>Task.Yield()</c>.
    /// </remarks>
    private void Run(Func<CancellationToken, Task> work)
    {
        _sectionWork.Add(work(_shellLifetime.Token));
    }

    /// <summary>
    /// Stops the guide import before the container that owns its database goes away.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _shellLifetime.CancelAsync().ConfigureAwait(false);
        await _guideImport.DrainAsync().ConfigureAwait(false);

        _shellLifetime.Dispose();
    }
}
