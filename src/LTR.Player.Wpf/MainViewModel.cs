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
/// Deliberately thin now. Opening a stream and remembering a position belong to
/// <see cref="PlaybackCoordinator"/>, running a guide import to <see cref="GuideImportCoordinator"/>; what is
/// left here is composition, the section selection, and commands that are two lines each. It had grown to
/// four responsibilities twice, and both times the same way — by being the only place that could reach
/// everything.
/// </para>
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, ISourceCoordinator, IAsyncDisposable
{
    private readonly PlaybackCoordinator _playback;
    private readonly GuideImportCoordinator _guideImport;
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
        GuideImportCoordinator guideImport,
        ILogger<MainViewModel> logger)
    {
        SourceManagement = sources;
        Channels = channels;
        Guide = guide;
        Movies = movies;
        SeriesCatalogue = series;
        ContinueWatching = continueWatching;
        Status = status;

        _playback = playback;
        _guideImport = guideImport;
        _logger = logger;

        Channels.PropertyChanged += OnChannelListPropertyChanged;
        SourceManagement.PropertyChanged += OnSourceManagementPropertyChanged;
        Movies.PropertyChanged += OnMovieListPropertyChanged;
        SeriesCatalogue.PropertyChanged += OnSeriesPropertyChanged;
        ContinueWatching.PropertyChanged += OnContinueWatchingPropertyChanged;
        _guideImport.PropertyChanged += OnGuideImportPropertyChanged;
        _playback.PropertyChanged += OnPlaybackPropertyChanged;

        SourceManagement.Coordinator = this;

        // The coordinator writes positions; the three lists that display one are known only here.
        _playback.ProgressRecorded = RefreshWhatShowsProgressAsync;
    }

    public SourceManagementViewModel SourceManagement { get; }

    public ChannelListViewModel Channels { get; }

    public GuideViewModel Guide { get; }

    public MovieListViewModel Movies { get; }

    public SeriesCatalogueViewModel SeriesCatalogue { get; }

    public ContinueWatchingViewModel ContinueWatching { get; }

    public StatusLine Status { get; }

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

    /// <summary>Samples where playback has reached. Driven by a timer the window owns.</summary>
    public void ObservePlaybackPosition()
    {
        _playback.ObservePosition();
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
        return _playback.StopAsync(cancellationToken);
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

        await _playback.PlayChannelAsync(source, item.Channel, item.Name, cancellationToken)
            .ConfigureAwait(true);
    }

    private bool HasSelectedChannel()
    {
        return Channels.SelectedChannel is not null;
    }

    /// <summary>Plays the selected film, picking up where it was left if it was started before.</summary>
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(HasSelectedMovie))]
    private Task PlayMovieAsync(CancellationToken cancellationToken)
    {
        return PlaySelectedMovieAsync(fromStart: false, cancellationToken);
    }

    /// <summary>Plays the selected film from the beginning, ignoring its resume point.</summary>
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

        await _playback
            .PlayEpisodeAsync(
                source,
                episode.Episode,
                ResumeFrom(episode.Episode.ResumePositionSeconds),
                $"{OpenSeriesName()}{episode.Label} · {episode.Title}",
                cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// Resumes a continue-watching entry, whichever kind it is.
    /// </summary>
    /// <remarks>
    /// The entry holds the identity of a film or of an episode, never of a series, so the item it refers to
    /// is loaded and played directly. Nothing about its series or season is needed.
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ResumeEntryAsync(ContinueWatchingEntry? entry, CancellationToken cancellationToken)
    {
        if (entry is null || SourceManagement.SelectedSource is not { } source)
        {
            return;
        }

        var startAt = ResumePolicy.StartFrom(entry.Position);

        if (entry.Kind == ContentKind.Movie)
        {
            var movie = await ContinueWatching.FindMovieAsync(entry.ItemId, cancellationToken)
                .ConfigureAwait(true);

            if (movie is null)
            {
                Status.Text = "That film is no longer in the catalogue.";
                return;
            }

            await _playback.PlayMovieAsync(source, movie, startAt, movie.Name, cancellationToken)
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

        await _playback
            .PlayEpisodeAsync(source, episode, startAt, $"{entry.Title} · {entry.Subtitle}", cancellationToken)
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

        // Anything being followed has to stop being followed, or stopping playback afterwards would write
        // the position straight back.
        _playback.StopFollowing();

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
        return _playback.StopAsync(cancellationToken);
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

        await _playback.PlayMovieAsync(source, row.Movie, startAt, row.Name, cancellationToken)
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

    /// <remarks>
    /// The overlay binds <see cref="NowPlaying"/> here rather than reaching into the coordinator, so the
    /// change has to be forwarded — the same object-boundary problem as the command guards above.
    /// </remarks>
    private void OnPlaybackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(PlaybackCoordinator.NowPlaying))
        {
            OnPropertyChanged(nameof(NowPlaying));
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
