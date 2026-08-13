using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Core.Sources;
using LTR.Playback;
using LTR.Providers;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Drives the main window: composes source management and the channel list, and owns playback.
/// </summary>
/// <remarks>
/// It is also the two halves' <see cref="ISourceCoordinator"/>, which is what keeps them from knowing
/// about one another. Only this class can both reach the channel list and stop a stream, and those are
/// exactly the two things source management has to trigger.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, ISourceCoordinator
{
    private readonly IProviderRegistry _providers;
    private readonly IPlaybackSession _session;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private string _nowPlaying = string.Empty;

    public MainViewModel(
        SourceManagementViewModel sources,
        ChannelListViewModel channels,
        StatusLine status,
        IProviderRegistry providers,
        IPlaybackSession session,
        ILogger<MainViewModel> logger)
    {
        SourceManagement = sources;
        Channels = channels;
        Status = status;

        _providers = providers;
        _session = session;
        _logger = logger;

        Channels.PropertyChanged += OnChannelListPropertyChanged;
        _session.StateChanged += OnPlaybackStateChanged;

        SourceManagement.Coordinator = this;
    }

    public SourceManagementViewModel SourceManagement { get; }

    public ChannelListViewModel Channels { get; }

    public StatusLine Status { get; }

    /// <summary>
    /// Loads the configured sources, so a restart lands straight in the channel list.
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return SourceManagement.InitializeAsync(cancellationToken);
    }

    /// <summary>
    /// Hands the provider connection back before the window goes away.
    /// </summary>
    /// <remarks>
    /// Not a command, because it is not a user action and must not be cancellable: a subscription
    /// permitting a single connection is unusable for minutes if the player exits still holding one.
    /// </remarks>
    public Task ShutdownAsync()
    {
        return ReleasePlaybackAsync(CancellationToken.None);
    }

    async Task ISourceCoordinator.ShowCatalogueAsync(PlaylistSource? source, CancellationToken cancellationToken)
    {
        try
        {
            await Channels.ShowAsync(source, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Cancellation is excluded on purpose: a cancelled refresh has its own wording, and it is
            // not a failure of the stored catalogue.
            PlayerLog.CatalogueLoadFailed(_logger, exception, source?.Name ?? string.Empty);
            Status.Text = $"The stored catalogue for {source?.Name} could not be read.";
        }
    }

    Task ISourceCoordinator.ReleasePlaybackAsync(CancellationToken cancellationToken)
    {
        return ReleasePlaybackAsync(cancellationToken);
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

    private async Task ReleasePlaybackAsync(CancellationToken cancellationToken)
    {
        await _session.StopAsync(cancellationToken).ConfigureAwait(true);
        NowPlaying = string.Empty;
    }

    /// <summary>
    /// Keeps the play command's guard current with a selection it does not own.
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

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        if (e.Current == PlaybackState.Playing)
        {
            Status.Text = $"Playing {NowPlaying}";
        }
    }
}
