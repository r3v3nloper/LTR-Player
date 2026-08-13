using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Catalogue;
using LTR.Core.Sources;
using LTR.Playback;
using LTR.Providers;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Drives the main window: composes source management, the channel list and the guide, and owns playback.
/// </summary>
/// <remarks>
/// It is also the halves' <see cref="ISourceCoordinator"/>, which is what keeps them from knowing about one
/// another. Only this class can reach the channel list, the guide and the stream at once, and those are
/// exactly the things source management has to trigger.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, ISourceCoordinator, IAsyncDisposable
{
    private readonly IProviderRegistry _providers;
    private readonly IPlaybackSession _session;
    private readonly IGuideImportService _guideImport;
    private readonly ILogger<MainViewModel> _logger;

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
    /// The import in flight, so a second one is not started alongside it and so shutdown can wait for it
    /// to notice its cancellation.
    /// </summary>
    private Task _guideImportTask = Task.CompletedTask;

    [ObservableProperty]
    private string _nowPlaying = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportGuideCommand))]
    private bool _isImportingGuide;

    public MainViewModel(
        SourceManagementViewModel sources,
        ChannelListViewModel channels,
        GuideViewModel guide,
        StatusLine status,
        IProviderRegistry providers,
        IPlaybackSession session,
        IGuideImportService guideImport,
        ILogger<MainViewModel> logger)
    {
        SourceManagement = sources;
        Channels = channels;
        Guide = guide;
        Status = status;

        _providers = providers;
        _session = session;
        _guideImport = guideImport;
        _logger = logger;

        Channels.PropertyChanged += OnChannelListPropertyChanged;
        SourceManagement.PropertyChanged += OnSourceManagementPropertyChanged;
        _session.StateChanged += OnPlaybackStateChanged;

        SourceManagement.Coordinator = this;
    }

    public SourceManagementViewModel SourceManagement { get; }

    public ChannelListViewModel Channels { get; }

    public GuideViewModel Guide { get; }

    public StatusLine Status { get; }

    /// <summary>
    /// The guide import in flight, or an already completed task.
    /// </summary>
    /// <remarks>
    /// Exposed because a background task nothing can observe is also a background task nothing can shut
    /// down or test. <see cref="DisposeAsync"/> waits on it, and so does anything that needs to know the
    /// import has finished.
    /// </remarks>
    public Task GuideImportCompletion => _guideImportTask;

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
    /// Hands the provider connection back before the window goes away.
    /// </summary>
    /// <remarks>
    /// Not a command, because it is not a user action. The playback release itself is deliberately not
    /// cancellable — a subscription permitting a single connection is unusable for minutes if the player
    /// exits still holding one — but everything else the shell has in flight is abandoned first, so closing
    /// the window does not wait on a catalogue load or a guide download.
    /// </remarks>
    public async Task ShutdownAsync()
    {
        await _shellLifetime.CancelAsync().ConfigureAwait(true);
        await ReleasePlaybackAsync(CancellationToken.None).ConfigureAwait(true);
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

    Task ISourceCoordinator.ReleasePlaybackAsync(CancellationToken cancellationToken)
    {
        return ReleasePlaybackAsync(cancellationToken);
    }

    void ISourceCoordinator.CatalogueImported(PlaylistSource source)
    {
        StartGuideImport(source, onlyWhenStale: true);
    }

    /// <remarks>
    /// Concurrent execution is allowed deliberately. The generated command would otherwise report
    /// CanExecute as false while a stream is still opening, so zapping away from a slow channel would
    /// be silently ignored — and the playback session's supersession handling, which exists precisely
    /// to make rapid channel changes safe, would never be reachable from the UI.
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(HasSelectedChannel))]
    private async Task PlaySelectedAsync(CancellationToken cancellationToken)
    {
        if (Channels.SelectedChannel is not { } item || SourceManagement.SelectedSource is not { } source)
        {
            return;
        }

        var request = _providers.GetStreamUrlResolver(source).ResolveLive(source, item.Channel);
        NowPlaying = item.Name;

        try
        {
            await _session.SwitchToAsync(request, cancellationToken).ConfigureAwait(true);
        }
        catch (PlaybackFailedException exception)
        {
            // Expected in daily use: providers take channels offline without notice.
            PlayerLog.ChannelUnplayable(_logger, exception, item.Name);
            Status.Text = $"{item.Name} could not be played. The channel may be offline.";
            NowPlaying = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Zapping onwards cancels the open that was still in flight. That is the intended
            // behaviour of a channel change, not a failure — and left unhandled it surfaces as an
            // error dialog for an ordinary key press.
        }
    }

    private bool HasSelectedChannel()
    {
        return Channels.SelectedChannel is not null;
    }

    [RelayCommand]
    private Task StopAsync(CancellationToken cancellationToken)
    {
        return ReleasePlaybackAsync(cancellationToken);
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
        return !IsImportingGuide && SourceManagement.SelectedSource is not null;
    }

    /// <summary>
    /// Runs a guide import in the background and reports it through the status line.
    /// </summary>
    /// <remarks>
    /// Not awaited by its caller, which is the point: an import takes minutes and the window has to stay
    /// usable throughout, including for playback. What keeps that from being fire-and-forget in the bad
    /// sense is that the task is kept, every failure is caught and reported here, and shutdown cancels it.
    /// </remarks>
    private void StartGuideImport(PlaylistSource source, bool onlyWhenStale)
    {
        if (IsImportingGuide)
        {
            return;
        }

        IsImportingGuide = true;
        _guideImportTask = RunGuideImportAsync(source, onlyWhenStale);
    }

    private async Task RunGuideImportAsync(PlaylistSource source, bool onlyWhenStale)
    {
        var progress = new Progress<GuideImportStage>(stage => Status.Text = Describe(stage));

        try
        {
            var result = onlyWhenStale
                ? await _guideImport
                    .ImportIfStaleAsync(source, progress, _shellLifetime.Token)
                    .ConfigureAwait(true)
                : await _guideImport
                    .ImportAsync(source, progress, _shellLifetime.Token)
                    .ConfigureAwait(true);

            Status.Text = Describe(result, source);

            if (result.Succeeded)
            {
                await Channels.RefreshGuideAsync(_shellLifetime.Token).ConfigureAwait(true);

                if (Guide.IsVisible)
                {
                    await Guide.LoadAsync(_shellLifetime.Token).ConfigureAwait(true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Either the window is closing or the source was removed. Neither is worth reporting.
        }
        catch (Exception exception)
        {
            PlayerLog.GuideImportFailed(_logger, exception, source.Name);
            Status.Text = "The programme guide could not be loaded. Details are in the log.";
        }
        finally
        {
            IsImportingGuide = false;
        }
    }

    private static string Describe(GuideImportStage stage)
    {
        return stage switch
        {
            GuideImportStage.Locating => "Looking for the programme guide...",
            GuideImportStage.Reading => "Reading the programme guide...",
            GuideImportStage.Matching => "Matching the guide to the channel list...",
            GuideImportStage.Pruning => "Tidying up the guide...",
            _ => "Working...",
        };
    }

    private static string Describe(GuideImportResult result, PlaylistSource source)
    {
        return result.Outcome switch
        {
            GuideImportOutcome.Imported when result.MatchedChannelCount == 0 =>
                "The guide loaded but matched none of the channels. Its channel names do not resemble "
                + "this subscription's.",
            GuideImportOutcome.Imported =>
                $"Guide loaded: {result.ProgrammeCount} programmes on {result.MatchedChannelCount} channels.",
            GuideImportOutcome.NoGuideAvailable => $"{source.Name} offers no programme guide.",
            GuideImportOutcome.Empty =>
                "The guide address answered with something that is not a programme guide.",
            _ => "The stored programme guide is already up to date.",
        };
    }

    private async Task ReleasePlaybackAsync(CancellationToken cancellationToken)
    {
        await _session.StopAsync(cancellationToken).ConfigureAwait(true);
        NowPlaying = string.Empty;
    }

    /// <summary>
    /// Combines a caller's token with the shell's, so anything the shell starts ends when the window does.
    /// </summary>
    private CancellationTokenSource LinkedToShellLifetime(CancellationToken cancellationToken)
    {
        return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shellLifetime.Token);
    }

    /// <summary>
    /// Keeps commands that guard on state they do not own current.
    /// </summary>
    /// <remarks>
    /// <c>[NotifyCanExecuteChangedFor]</c> cannot cross an object boundary: the command is here and the
    /// property its guard reads belongs to the channel list. Without this the button keeps whatever
    /// state it had when the window opened — the defect class that shipped three times, and the reason
    /// the tests assert the notification rather than <c>CanExecute</c>.
    /// </remarks>
    private void OnChannelListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // An empty name means every property, which WPF and the toolkit both use to mean "re-read all".
        if (e.PropertyName is not (null or "" or nameof(ChannelListViewModel.SelectedChannel)))
        {
            return;
        }

        PlaySelectedCommand.NotifyCanExecuteChanged();
    }

    /// <remarks>
    /// The same boundary problem as above: <see cref="ImportGuideCommand"/> lives here and guards on the
    /// selected source, which belongs to source management.
    /// </remarks>
    private void OnSourceManagementPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or "" or nameof(SourceManagementViewModel.SelectedSource)))
        {
            return;
        }

        ImportGuideCommand.NotifyCanExecuteChanged();
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        if (e.Current == PlaybackState.Playing)
        {
            Status.Text = $"Playing {NowPlaying}";
        }
    }

    /// <summary>
    /// Stops the guide import before the container that owns its database goes away.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _shellLifetime.CancelAsync().ConfigureAwait(false);

        try
        {
            await _guideImportTask.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Already reported by the import itself; failing to shut down over it would be worse.
            PlayerLog.GuideImportFailed(_logger, exception, string.Empty);
        }

        _shellLifetime.Dispose();
    }
}
