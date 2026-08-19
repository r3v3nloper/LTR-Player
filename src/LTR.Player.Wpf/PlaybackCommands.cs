using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LTR.Core.Content;
using LTR.Core.Playback;
using Microsoft.Extensions.Logging;

namespace LTR.Player.Wpf;

/// <summary>
/// Turns what the viewer has selected into something to play.
/// </summary>
/// <remarks>
/// <para>
/// The third and last piece of what used to be one class. <see cref="PlaybackCoordinator"/> opens a stream and
/// remembers where it got to; this decides *which item*, from which list, and at what position; the shell is
/// left with composition, the section selection and the guide. Splitting here rather than anywhere else is what
/// the boundary already was — every method here reads a selection and calls the coordinator, and nothing here
/// touches an engine.
/// </para>
/// <para>
/// It reads all four sections, which is the honest cost and is not the same failing the shell had. Reading a
/// selection is the whole job; what it deliberately cannot do is reach the guide, the settings pane, the
/// window's lifetime or the section availability rules. The shell keeps those, and keeps being the only thing
/// that holds both this and them.
/// </para>
/// <para>
/// Concurrent execution is allowed on every command that opens a stream, deliberately. A generated command
/// otherwise reports <c>CanExecute</c> as false while one is still opening, so zapping away from a slow channel
/// would be silently ignored — and <c>PlaybackSession</c>'s supersession handling, which exists precisely to
/// make rapid changes safe, would never be reachable from the window.
/// </para>
/// </remarks>
public sealed partial class PlaybackCommands
{
    private readonly SourceManagementViewModel _sources;
    private readonly ChannelListViewModel _channels;
    private readonly MovieListViewModel _movies;
    private readonly SeriesCatalogueViewModel _series;
    private readonly ContinueWatchingViewModel _continueWatching;
    private readonly PlaybackCoordinator _playback;
    private readonly StatusLine _status;
    private readonly ILogger<PlaybackCommands> _logger;

    /// <summary>
    /// The notifications this class carries from a section to a command guard that reads it.
    /// </summary>
    /// <remarks>
    /// Its own table rather than the shell's, and that is most of why the commands are worth moving: every
    /// forward here exists because a guard here reads a section's selection, and they were previously mixed in
    /// with the shell's own forwards for the guide and the panes.
    /// </remarks>
    private readonly CrossObjectNotifications _notifications;

    public PlaybackCommands(
        SourceManagementViewModel sources,
        ChannelListViewModel channels,
        MovieListViewModel movies,
        SeriesCatalogueViewModel series,
        ContinueWatchingViewModel continueWatching,
        PlaybackCoordinator playback,
        StatusLine status,
        ILogger<PlaybackCommands> logger)
    {
        _sources = sources;
        _channels = channels;
        _movies = movies;
        _series = series;
        _continueWatching = continueWatching;
        _playback = playback;
        _status = status;
        _logger = logger;

        // Nothing is raised on this object itself, so the table has nothing to raise: every registration below
        // notifies a command rather than recomputing a property.
        _notifications = new CrossObjectNotifications(_ => { });
        RegisterNotificationForwards();

        // The coordinator writes positions; the three lists that display one are known here.
        _playback.ProgressRecorded = RefreshWhatShowsProgressAsync;
    }

    /// <summary>
    /// Brings the channel list to the front, for a zap that started from another section.
    /// </summary>
    /// <remarks>
    /// A delegate because the section selection is the shell's — it decides what the left pane shows, and this
    /// class deliberately cannot. Inert until the shell assigns it, as
    /// <see cref="PlaybackCoordinator.ProgressRecorded"/> is.
    /// </remarks>
    public Action ShowChannelList { get; set; } = () => { };

    /// <summary>
    /// Moves the given number of places through whatever is playing and plays what it lands on.
    /// </summary>
    /// <remarks>
    /// Public because the keyboard reaches it through <see cref="PlayerActions"/> rather than through a
    /// command: the shell hands this over as a delegate, along with <see cref="StopAsync"/>.
    /// </remarks>
    public Task PlayAdjacentAsync(int offset, CancellationToken cancellationToken)
    {
        return _playback.NowPlayingItem switch
        {
            { Kind: ContentKind.Series, Episode: { } episode } =>
                PlayAdjacentEpisodeAsync(episode, offset, cancellationToken),
            { Kind: ContentKind.Movie } => Task.CompletedTask,
            _ => ZapAsync(offset, cancellationToken),
        };
    }

    /// <summary>
    /// Rereads the three places a stored position is displayed.
    /// </summary>
    /// <remarks>
    /// A resume point appears on a film row, on an episode row and as a continue-watching entry. Any change
    /// to one has to reach all three, or the same position is offered in one place and gone from another.
    /// </remarks>
    public async Task RefreshWhatShowsProgressAsync(CancellationToken cancellationToken)
    {
        await _movies.RefreshSelectedAsync(cancellationToken).ConfigureAwait(true);
        await _series.RefreshOpenSeriesAsync(cancellationToken).ConfigureAwait(true);
        await _continueWatching.ReloadAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(HasSelectedChannel))]
    private async Task PlaySelectedAsync(CancellationToken cancellationToken)
    {
        if (_channels.SelectedChannel is not { } item || _sources.SelectedSource is not { } source)
        {
            return;
        }

        await _playback.PlayChannelAsync(source, item.Channel, item.Name, cancellationToken)
            .ConfigureAwait(true);
    }

    private bool HasSelectedChannel()
    {
        return _channels.SelectedChannel is not null;
    }

    /// <summary>Plays the next thing of whatever kind is playing.</summary>
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanPlayAdjacent))]
    private Task PlayNextAsync(CancellationToken cancellationToken)
    {
        return PlayAdjacentAsync(offset: 1, cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanPlayAdjacent))]
    private Task PlayPreviousAsync(CancellationToken cancellationToken)
    {
        return PlayAdjacentAsync(offset: -1, cancellationToken);
    }

    /// <summary>
    /// Whether the thing playing has a next and a previous at all.
    /// </summary>
    /// <remarks>
    /// A film does not: it is one item, and the film catalogue's order is a search result rather than a
    /// sequence anybody watches through. So the buttons grey out for a film instead of doing something the
    /// viewer did not ask for — which is how this whole pair came to be looked at.
    /// </remarks>
    private bool CanPlayAdjacent()
    {
        return _playback.NowPlayingItem?.Kind != ContentKind.Movie;
    }

    /// <remarks>
    /// The section does the looking up, because it owns the store access for series. Silent at the ends of a
    /// series in the picture but not in the status line: running out of episodes is worth saying, where
    /// running out of channels is not — the channel list shows its own ends.
    /// </remarks>
    private async Task PlayAdjacentEpisodeAsync(
        Episode current,
        int offset,
        CancellationToken cancellationToken)
    {
        var adjacent = await _series
            .FindAdjacentEpisodeAsync(current.Id, offset, cancellationToken)
            .ConfigureAwait(true);

        if (adjacent is null)
        {
            _status.Text = offset > 0
                ? "That was the last episode of the series."
                : "That was the first episode of the series.";

            return;
        }

        await PlayEpisodeAsync(adjacent, cancellationToken).ConfigureAwait(true);
    }

    /// <remarks>
    /// Zapping only makes sense in the channel list, so it switches back to it — pressing next-channel while
    /// looking at the film catalogue and having the picture change but the list not is disorienting. Nothing
    /// is played when the selection could not move, which is what makes the ends of the list quiet rather
    /// than reopening the same channel.
    /// </remarks>
    private async Task ZapAsync(int offset, CancellationToken cancellationToken)
    {
        ShowChannelList();

        if (!_channels.SelectAdjacent(offset))
        {
            return;
        }

        await PlaySelectedAsync(cancellationToken).ConfigureAwait(true);
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
        return _movies.SelectedMovie is not null;
    }

    private bool CanRestartMovie()
    {
        return CurrentMovie()?.HasResumePoint ?? false;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task PlayEpisodeAsync(EpisodeItemViewModel? episode, CancellationToken cancellationToken)
    {
        if (episode is null || _sources.SelectedSource is not { } source)
        {
            return;
        }

        await _playback
            .PlayEpisodeAsync(
                source,
                episode.Episode,
                ResumeFrom(episode.Episode.ResumePositionSeconds),
                episode.NowPlaying,
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
        if (entry is null || _sources.SelectedSource is not { } source)
        {
            return;
        }

        var startAt = ResumePolicy.StartFrom(entry.Position);

        if (entry.Kind == ContentKind.Movie)
        {
            var movie = await _continueWatching.FindMovieAsync(entry.ItemId, cancellationToken)
                .ConfigureAwait(true);

            if (movie is null)
            {
                _status.Text = "That film is no longer in the catalogue.";
                return;
            }

            await _playback.PlayMovieAsync(source, movie, startAt, movie.Name, cancellationToken)
                .ConfigureAwait(true);

            return;
        }

        var episode = await _continueWatching.FindEpisodeAsync(entry.ItemId, cancellationToken)
            .ConfigureAwait(true);

        if (episode is null)
        {
            _status.Text = "That episode is no longer in the catalogue.";
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
            await _continueWatching.ForgetAsync(entry, cancellationToken).ConfigureAwait(true);
            await RefreshWhatShowsProgressAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // The window is closing.
        }
        catch (Exception exception)
        {
            PlayerLog.ProgressNotRecorded(_logger, exception, entry.Kind.ToString(), entry.ItemId);
            _status.Text = "That could not be taken off the list. Details are in the log.";
        }
    }

    /// <summary>Releases the stream, which is also what makes previous and next mean channels again.</summary>
    [RelayCommand]
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _playback.StopAsync(cancellationToken);
    }

    private async Task PlaySelectedMovieAsync(bool fromStart, CancellationToken cancellationToken)
    {
        if (CurrentMovie() is not { } row || _sources.SelectedSource is not { } source)
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
        return _movies.DetailedMovie ?? _movies.SelectedMovie;
    }

    private static TimeSpan? ResumeFrom(int? resumePositionSeconds)
    {
        return resumePositionSeconds is { } seconds and > 0
            ? ResumePolicy.StartFrom(TimeSpan.FromSeconds(seconds))
            : null;
    }

    /// <summary>
    /// States every notification a guard here needs carried across an object boundary.
    /// </summary>
    /// <remarks>
    /// <c>[NotifyCanExecuteChangedFor]</c> cannot cross one: the command is here and the property its guard
    /// reads belongs to a section or to the coordinator. Without the forward the button keeps whatever state it
    /// had when the window opened — the defect class that shipped three times, and the reason the tests assert
    /// the notification rather than <c>CanExecute</c>.
    /// </remarks>
    private void RegisterNotificationForwards()
    {
        _notifications.When(_channels, nameof(ChannelListViewModel.SelectedChannel))
            .Notifies(PlaySelectedCommand);

        _notifications.When(_continueWatching, nameof(ContinueWatchingViewModel.SelectedEntry))
            .Notifies(ResumeEntryCommand);

        _notifications
            .When(_movies, nameof(MovieListViewModel.SelectedMovie))
            .Notifies(PlayMovieCommand)
            .Notifies(RestartMovieCommand);

        // A second, separate reason: the guard reads the resume position, which only the detail carries.
        _notifications.When(_movies, nameof(MovieListViewModel.DetailedMovie)).Notifies(RestartMovieCommand);

        // What previous and next may do depends on what kind of thing is playing, and only the coordinator
        // knows that. It has to notify *both*: a film closes the two buttons and a stop reopens them.
        _notifications
            .When(_playback, nameof(PlaybackCoordinator.NowPlayingItem))
            .Notifies(PlayNextCommand)
            .Notifies(PlayPreviousCommand);
    }
}
