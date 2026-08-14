using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Playback;
using LTR.Providers;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Drives the main window: composes the catalogue sections and the guide, and owns playback.
/// </summary>
/// <remarks>
/// It is also the sections' <see cref="ISourceCoordinator"/>, which is what keeps them from knowing about
/// one another. Only this class can reach the lists, the guide and the stream at once, and those are
/// exactly the things source management has to trigger.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, ISourceCoordinator, IAsyncDisposable
{
    private readonly IProviderRegistry _providers;
    private readonly IPlaybackSession _session;
    private readonly GuideImportCoordinator _guideImport;
    private readonly WatchProgressRecorder _progress;
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

    [ObservableProperty]
    private string _nowPlaying = string.Empty;

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
        IProviderRegistry providers,
        IPlaybackSession session,
        GuideImportCoordinator guideImport,
        WatchProgressRecorder progress,
        ILogger<MainViewModel> logger)
    {
        SourceManagement = sources;
        Channels = channels;
        Guide = guide;
        Movies = movies;
        SeriesCatalogue = series;
        ContinueWatching = continueWatching;
        Status = status;

        _providers = providers;
        _session = session;
        _guideImport = guideImport;
        _progress = progress;
        _logger = logger;

        Channels.PropertyChanged += OnChannelListPropertyChanged;
        SourceManagement.PropertyChanged += OnSourceManagementPropertyChanged;
        Movies.PropertyChanged += OnMovieListPropertyChanged;
        SeriesCatalogue.PropertyChanged += OnSeriesPropertyChanged;
        ContinueWatching.PropertyChanged += OnContinueWatchingPropertyChanged;
        _guideImport.PropertyChanged += OnGuideImportPropertyChanged;
        _session.StateChanged += OnPlaybackStateChanged;

        SourceManagement.Coordinator = this;
    }

    public SourceManagementViewModel SourceManagement { get; }

    public ChannelListViewModel Channels { get; }

    public GuideViewModel Guide { get; }

    public MovieListViewModel Movies { get; }

    public SeriesCatalogueViewModel SeriesCatalogue { get; }

    public ContinueWatchingViewModel ContinueWatching { get; }

    public StatusLine Status { get; }

    /// <summary>The guide import in flight, or an already completed task.</summary>
    public Task GuideImportCompletion => _guideImport.Completion;

    public bool IsImportingGuide => _guideImport.IsImporting;

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
    /// Samples where playback has reached, so a position survives the stream being closed.
    /// </summary>
    /// <remarks>
    /// Driven by a timer, because by the time playback has stopped the engine no longer has a position to
    /// report — a recorder that only looked when asked to save would always save nothing.
    /// </remarks>
    public void ObservePlaybackPosition()
    {
        if (_progress.IsTracking)
        {
            _progress.Observe(_session.Position, _session.Duration);
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
        // Sampled once more first: the last tick may be seconds old, and those seconds are the viewer's
        // place in the film they were watching when they closed the window.
        ObservePlaybackPosition();

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

        // Recorded before the switch, while the samples still describe what was playing. A channel has
        // nothing to record, so this is a no-op unless a film was open.
        await RecordProgressAsync(cancellationToken).ConfigureAwait(true);

        var request = _providers.GetStreamUrlResolver(source).ResolveLive(source, item.Channel);
        await StartAsync(request, item.Name, cancellationToken).ConfigureAwait(true);
    }

    private bool HasSelectedChannel()
    {
        return Channels.SelectedChannel is not null;
    }

    /// <summary>
    /// Plays the selected film, picking up where it was left if it was started before.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(HasSelectedMovie))]
    private Task PlayMovieAsync(CancellationToken cancellationToken)
    {
        return PlaySelectedMovieAsync(fromStart: false, cancellationToken);
    }

    /// <summary>Plays the selected film from the beginning, discarding its resume point.</summary>
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanRestartMovie))]
    private Task RestartMovieAsync(CancellationToken cancellationToken)
    {
        return PlaySelectedMovieAsync(fromStart: true, cancellationToken);
    }

    private bool HasSelectedMovie()
    {
        return Movies.SelectedMovie is not null;
    }

    private bool CanRestartMovie()
    {
        return CurrentMovie()?.HasResumePoint ?? false;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PlayEpisodeAsync(EpisodeItemViewModel? episode, CancellationToken cancellationToken)
    {
        if (episode is null || SourceManagement.SelectedSource is not { } source)
        {
            return;
        }

        var startAt = ResumeFrom(episode.Episode.ResumePositionSeconds);
        var request = _providers.GetStreamUrlResolver(source)
            .ResolveEpisode(source, episode.Episode, startAt);

        await PlayVodAsync(
                ContentKind.Series,
                episode.Id,
                request,
                $"{OpenSeriesName()}{episode.Label} · {episode.Title}",
                startAt,
                cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Resumes a continue-watching entry, whichever kind it is.
    /// </summary>
    /// <remarks>
    /// The entry holds the identity of a film or of an episode, never of a series, so the item it refers to
    /// is loaded and resolved directly. Nothing about its series or season is needed to play it.
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ResumeEntryAsync(ContinueWatchingEntry? entry, CancellationToken cancellationToken)
    {
        if (entry is null || SourceManagement.SelectedSource is not { } source)
        {
            return;
        }

        var startAt = ResumePolicy.StartFrom(entry.Position);
        var resolver = _providers.GetStreamUrlResolver(source);

        if (entry.Kind == ContentKind.Movie)
        {
            var movie = await ContinueWatching.FindMovieAsync(entry.ItemId, cancellationToken)
                .ConfigureAwait(true);

            if (movie is null)
            {
                Status.Text = "That film is no longer in the catalogue.";
                return;
            }

            await PlayVodAsync(
                    ContentKind.Movie,
                    movie.Id,
                    resolver.ResolveMovie(source, movie, startAt),
                    movie.Name,
                    startAt,
                    cancellationToken)
                .ConfigureAwait(true);

            return;
        }

        var episode = await ContinueWatching.FindEpisodeAsync(entry.ItemId, cancellationToken)
            .ConfigureAwait(true);

        if (episode is null)
        {
            Status.Text = "That episode is no longer in the catalogue.";
            return;
        }

        await PlayVodAsync(
                ContentKind.Series,
                episode.Id,
                resolver.ResolveEpisode(source, episode, startAt),
                $"{entry.Title} · {entry.Subtitle}",
                startAt,
                cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Takes an entry off the continue-watching list.
    /// </summary>
    /// <remarks>
    /// Here rather than on the section alone, because the resume point it forgets is shown in two more
    /// places: the film row's "Resume at" line and the episode list's. Forgetting it in one and leaving it in
    /// the others is the kind of disagreement that reads as the removal not having worked.
    /// </remarks>
    [RelayCommand]
    private async Task ForgetEntryAsync(ContinueWatchingEntry? entry, CancellationToken cancellationToken)
    {
        if (entry is null)
        {
            return;
        }

        // Anything being followed for this item has to stop being followed, or stopping playback afterwards
        // would write the position straight back.
        if (_progress.IsTracking)
        {
            _progress.Forget();
        }

        try
        {
            await ContinueWatching.ForgetAsync(entry, cancellationToken).ConfigureAwait(true);
            await RefreshWhatShowsProgressAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The window is closing.
        }
        catch (Exception exception)
        {
            PlayerLog.ProgressNotRecorded(_logger, exception, entry.Kind.ToString(), entry.ItemId);
            Status.Text = "That could not be taken off the list. Details are in the log.";
        }
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

    private async Task PlaySelectedMovieAsync(bool fromStart, CancellationToken cancellationToken)
    {
        if (CurrentMovie() is not { } row || SourceManagement.SelectedSource is not { } source)
        {
            return;
        }

        var startAt = fromStart ? null : ResumeFrom(row.Movie.ResumePositionSeconds);
        var request = _providers.GetStreamUrlResolver(source).ResolveMovie(source, row.Movie, startAt);

        await PlayVodAsync(ContentKind.Movie, row.Id, request, row.Name, startAt, cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// The film whose detail is on screen, falling back to the selected row while it is still loading.
    /// </summary>
    private MovieItemViewModel? CurrentMovie()
    {
        return Movies.DetailedMovie ?? Movies.SelectedMovie;
    }

    private string OpenSeriesName()
    {
        return SeriesCatalogue.OpenSeries is { } series ? $"{series.Name} · " : string.Empty;
    }

    private static TimeSpan? ResumeFrom(int? resumePositionSeconds)
    {
        return resumePositionSeconds is { } seconds and > 0
            ? ResumePolicy.StartFrom(TimeSpan.FromSeconds(seconds))
            : null;
    }

    /// <summary>
    /// Starts a film or episode and begins following where it gets to.
    /// </summary>
    private async Task PlayVodAsync(
        ContentKind kind,
        int itemId,
        MediaRequest request,
        string displayName,
        TimeSpan? startAt,
        CancellationToken cancellationToken)
    {
        await RecordProgressAsync(cancellationToken).ConfigureAwait(true);

        // Tracked from the position playback was asked to start at, not from zero. A viewer who resumes at
        // forty minutes and closes the window before the first sample arrives would otherwise have their
        // place reset to the beginning.
        _progress.Track(kind, itemId, startAt ?? TimeSpan.Zero);

        if (!await StartAsync(request, displayName, cancellationToken).ConfigureAwait(true))
        {
            // Nothing was watched, so there is nothing to remember — and leaving the recorder tracking a
            // film that never opened would attribute the next stop to it.
            _progress.Forget();
        }
    }

    /// <summary>
    /// Opens a stream and reports whether it started.
    /// </summary>
    private async Task<bool> StartAsync(
        MediaRequest request,
        string displayName,
        CancellationToken cancellationToken)
    {
        NowPlaying = displayName;

        try
        {
            await _session.SwitchToAsync(request, cancellationToken).ConfigureAwait(true);
            return true;
        }
        catch (PlaybackFailedException exception)
        {
            // Expected in daily use: providers take channels offline without notice, and a subscription
            // permitting one connection refuses the next stream until it notices the last one closed.
            PlayerLog.ChannelUnplayable(_logger, exception, displayName);
            Status.Text = $"{displayName} could not be played. It may be offline, or the subscription's "
                + "one connection may still be in use.";
            NowPlaying = string.Empty;

            return false;
        }
        catch (OperationCanceledException)
        {
            // Zapping onwards cancels the open that was still in flight. That is the intended
            // behaviour of a channel change, not a failure — and left unhandled it surfaces as an
            // error dialog for an ordinary key press.
            return false;
        }
    }

    /// <summary>
    /// Writes down where the viewer got to, and brings what shows it up to date.
    /// </summary>
    private async Task RecordProgressAsync(CancellationToken cancellationToken)
    {
        if (!_progress.IsTracking)
        {
            return;
        }

        ObservePlaybackPosition();
        await _progress.RecordAsync(cancellationToken).ConfigureAwait(true);

        try
        {
            await RefreshWhatShowsProgressAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The window is closing; the position itself is already written.
        }
    }

    /// <summary>
    /// Rereads the three places a stored position is displayed.
    /// </summary>
    /// <remarks>
    /// A resume point appears on a film row, on an episode row and as a continue-watching entry. Any change
    /// to one has to reach all three, or the same position is offered in one place and gone from another.
    /// </remarks>
    private async Task RefreshWhatShowsProgressAsync(CancellationToken cancellationToken)
    {
        await Movies.RefreshSelectedAsync(cancellationToken).ConfigureAwait(true);
        await SeriesCatalogue.RefreshOpenSeriesAsync(cancellationToken).ConfigureAwait(true);
        await ContinueWatching.ReloadAsync(cancellationToken).ConfigureAwait(true);
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

    private async Task ReleasePlaybackAsync(CancellationToken cancellationToken)
    {
        await _session.StopAsync(cancellationToken).ConfigureAwait(true);
        NowPlaying = string.Empty;

        await RecordProgressAsync(cancellationToken).ConfigureAwait(true);
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

    /// <remarks>
    /// Also the place the film detail is fetched from. Selecting a film means a network call, which a
    /// property setter cannot await — so the section reports the selection and the shell, which owns the
    /// lifetime token, drives the work.
    /// </remarks>
    private void OnMovieListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case null or "" or nameof(MovieListViewModel.SelectedMovie):
                PlayMovieCommand.NotifyCanExecuteChanged();
                RestartMovieCommand.NotifyCanExecuteChanged();
                Run(Movies.LoadSelectedDetailAsync);
                break;

            case nameof(MovieListViewModel.DetailedMovie):
                RestartMovieCommand.NotifyCanExecuteChanged();
                break;

            case nameof(MovieListViewModel.SearchText) or nameof(MovieListViewModel.SelectedCategory):
                Run(Movies.SearchAsync);
                break;

            default:
                break;
        }
    }

    private void OnSeriesPropertyChanged(object? sender, PropertyChangedEventArgs e)
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

    private void OnContinueWatchingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(ContinueWatchingViewModel.SelectedEntry))
        {
            ResumeEntryCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnGuideImportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or "" or nameof(GuideImportCoordinator.IsImporting)))
        {
            return;
        }

        OnPropertyChanged(nameof(IsImportingGuide));
        ImportGuideCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Runs work triggered by a property change, which cannot be awaited where it is raised.
    /// </summary>
    /// <remarks>
    /// The task is deliberately not kept. Each of these reloads a list, is cancelled by the shell lifetime,
    /// and handles its own failures — so there is nothing for a caller to wait on and nothing that could
    /// escape as an unobserved exception. The last one to finish wins, which is also the one the viewer
    /// asked for last.
    /// </remarks>
    private void Run(Func<CancellationToken, Task> work)
    {
        _ = work(_shellLifetime.Token);
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
        await _guideImport.DrainAsync().ConfigureAwait(false);

        _shellLifetime.Dispose();
    }
}
