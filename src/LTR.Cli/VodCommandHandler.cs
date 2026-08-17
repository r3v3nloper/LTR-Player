using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Playback;
using LTR.Providers;

namespace LTR.Cli;

/// <summary>
/// Inspects a stored source's film and series catalogue, and plays one item headlessly.
/// </summary>
/// <remarks>
/// <para>
/// Works against a stored source rather than a panel address, because everything here is about what the
/// catalogue holds: which films were imported, whether a series' seasons can be fetched, and whether the
/// address built from a stored entry actually plays.
/// </para>
/// <para>
/// The figures worth reading are the ones an import can get wrong while appearing to succeed. A film
/// count of zero on a subscription that sells films means the capability probe said no; a series whose
/// episode list comes back empty means the panel used an episode shape this client could not read.
/// </para>
/// </remarks>
internal sealed class VodCommandHandler
{
    /// <summary>How many entries to print when no limit is given.</summary>
    private const int DefaultLimit = 40;

    private readonly ISourceStore _sources;
    private readonly IVodCatalogue _catalogue;
    private readonly IWatchProgressStore _watched;
    private readonly IVodDetailService _detail;
    private readonly IProviderRegistry _providers;
    private readonly IPlaybackSession _session;
    private readonly IStreamFailureExplainer _failures;
    private readonly ConnectionReleaseCheck _releaseCheck;
    private readonly WatchProgressRecorder _progress;

    /// <param name="progress">
    /// The same recorder the window uses. Shared rather than reimplemented, so that "how much counts as
    /// watched" cannot come out differently here than on screen.
    /// </param>
    public VodCommandHandler(
        ISourceStore sources,
        IVodCatalogue catalogue,
        IWatchProgressStore watched,
        IVodDetailService detail,
        IProviderRegistry providers,
        IPlaybackSession session,
        IStreamFailureExplainer failures,
        ConnectionReleaseCheck releaseCheck,
        WatchProgressRecorder progress)
    {
        _sources = sources;
        _catalogue = catalogue;
        _watched = watched;
        _detail = detail;
        _providers = providers;
        _session = session;
        _failures = failures;
        _releaseCheck = releaseCheck;
        _progress = progress;
    }

    public async Task<int> ListMoviesAsync(
        int sourceId,
        string? filter,
        int limit,
        CancellationToken cancellationToken)
    {
        if (await FindSourceAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
        {
            return 1;
        }

        var movies = await _catalogue.GetMoviesAsync(source.Id, cancellationToken).ConfigureAwait(false);
        var matching = Narrow(movies, filter, movie => movie.Name);

        Console.WriteLine($"Source     {source.Name}");
        Console.WriteLine($"Films      {movies.Count} stored, {matching.Count} matching");
        ReportSectionState(source, movies.Count, ContentKind.Movie);

        if (matching.Count == 0)
        {
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"{"id",-6} {"name",-44} {"year",-6} {"cont",-6} resume");

        foreach (var movie in matching.Take(Positive(limit)))
        {
            Console.WriteLine(
                $"{movie.Id,-6} {ConsoleText.Truncate(movie.Name, 44),-44} "
                + $"{movie.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",-6} "
                + $"{movie.ContainerExtension ?? "-",-6} {DescribeResume(movie.ResumePositionSeconds, movie.IsWatched)}");
        }

        return 0;
    }

    public async Task<int> ListSeriesAsync(
        int sourceId,
        string? filter,
        int limit,
        CancellationToken cancellationToken)
    {
        if (await FindSourceAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
        {
            return 1;
        }

        var series = await _catalogue.GetSeriesAsync(source.Id, cancellationToken).ConfigureAwait(false);
        var matching = Narrow(series, filter, item => item.Name);

        Console.WriteLine($"Source     {source.Name}");
        Console.WriteLine($"Series     {series.Count} stored, {matching.Count} matching");
        ReportSectionState(source, series.Count, ContentKind.Series);

        if (matching.Count == 0)
        {
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine($"{"id",-6} {"name",-44} {"year",-6} seasons fetched");

        foreach (var item in matching.Take(Positive(limit)))
        {
            Console.WriteLine(
                $"{item.Id,-6} {ConsoleText.Truncate(item.Name, 44),-44} "
                + $"{item.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",-6} "
                + $"{ConsoleText.FormatUtc(item.DetailFetchedUtc)}");
        }

        return 0;
    }

    /// <summary>
    /// Fetches a series' seasons and episodes if needed, and prints them.
    /// </summary>
    /// <remarks>
    /// The command that matters most here. Three shapes of episode listing are in circulation and a panel
    /// using an unreadable one produces a series with no episodes rather than an error, which is invisible
    /// from anywhere else.
    /// </remarks>
    public async Task<int> ShowSeriesAsync(int sourceId, int seriesId, CancellationToken cancellationToken)
    {
        if (await FindSourceAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
        {
            return 1;
        }

        var series = await _detail.GetSeriesAsync(source, seriesId, cancellationToken).ConfigureAwait(false);

        if (series is null)
        {
            Console.Error.WriteLine(
                $"No series with id {seriesId} in this source. Run 'vod series --source-id {sourceId}'.");
            return 1;
        }

        Console.WriteLine($"Series     {series.Name}");
        Console.WriteLine($"Provider   id {series.ExternalId}, changed {ConsoleText.FormatUtc(series.LastModifiedUtc)}");
        Console.WriteLine($"Detail     fetched {ConsoleText.FormatUtc(series.DetailFetchedUtc)}");
        Console.WriteLine($"Seasons    {series.Seasons.Count}");

        if (series.Seasons.Count == 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "The panel returned no episodes this client could read. That is either an empty series or "
                + "an episode listing in a shape the mapper does not recognise; the log records which.");
            return 1;
        }

        foreach (var season in series.Seasons)
        {
            Console.WriteLine();
            Console.WriteLine($"  Season {season.Number} — {season.Episodes.Count} episodes");

            foreach (var episode in season.Episodes)
            {
                Console.WriteLine(
                    $"    {episode.Id,-6} {EpisodeNaming.Label(season.Number, episode.Number),-8} "
                    + $"{ConsoleText.Truncate(episode.Title, 40),-40} {episode.ContainerExtension ?? "-",-5} "
                    + $"{DescribeResume(episode.ResumePositionSeconds, episode.IsWatched)}");
            }
        }

        return 0;
    }

    public async Task<int> ShowMovieAsync(int sourceId, int movieId, CancellationToken cancellationToken)
    {
        if (await FindSourceAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
        {
            return 1;
        }

        var movie = await _detail.GetMovieAsync(source, movieId, cancellationToken).ConfigureAwait(false);

        if (movie is null)
        {
            Console.Error.WriteLine(
                $"No film with id {movieId} in this source. Run 'vod list --source-id {sourceId}'.");
            return 1;
        }

        Console.WriteLine($"Film       {movie.Name}");
        Console.WriteLine($"Provider   id {movie.ExternalId}, container {movie.ContainerExtension ?? "unstated"}");
        Console.WriteLine($"Year       {movie.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}");
        Console.WriteLine($"Runtime    {DescribeDuration(movie.DurationSeconds)}");
        Console.WriteLine($"Detail     {(movie.HasDetail ? "fetched" : "not available")}");
        Console.WriteLine($"Resume     {DescribeResume(movie.ResumePositionSeconds, movie.IsWatched)}");

        if (!string.IsNullOrWhiteSpace(movie.Plot))
        {
            Console.WriteLine();
            Console.WriteLine(movie.Plot);
        }

        return 0;
    }

    public async Task<int> ContinueWatchingAsync(int sourceId, CancellationToken cancellationToken)
    {
        if (await FindSourceAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
        {
            return 1;
        }

        var entries = await _watched
            .GetContinueWatchingAsync(source.Id, DefaultLimit, cancellationToken)
            .ConfigureAwait(false);

        if (entries.Count == 0)
        {
            Console.WriteLine("Nothing is part-watched in this source.");
            return 0;
        }

        Console.WriteLine($"{"kind",-7} {"id",-6} {"title",-38} {"at",-9} {"of",-9} last watched");

        foreach (var entry in entries)
        {
            Console.WriteLine(
                $"{DescribeKind(entry.Kind),-7} {entry.ItemId,-6} "
                + $"{ConsoleText.Truncate(DescribeEntry(entry), 38),-38} "
                + $"{DescribeDuration(entry.PositionSeconds),-9} {DescribeDuration(entry.DurationSeconds),-9} "
                + $"{ConsoleText.FormatUtc(entry.LastWatchedUtc)}");
        }

        return 0;
    }

    /// <summary>
    /// Forgets where the viewer got to, taking an item off the continue-watching list.
    /// </summary>
    /// <remarks>
    /// The command-line counterpart of the list's own remove button, and what makes the resume loop
    /// verifiable end to end without the window: play with <c>--remember</c>, see it under
    /// <c>vod continue</c>, take it off with this.
    /// </remarks>
    public async Task<int> ForgetAsync(
        int sourceId,
        int? movieId,
        int? episodeId,
        CancellationToken cancellationToken)
    {
        if (await FindSourceAsync(sourceId, cancellationToken).ConfigureAwait(false) is null)
        {
            return 1;
        }

        if ((movieId is null) == (episodeId is null))
        {
            Console.Error.WriteLine("Pass exactly one of --movie-id and --episode-id.");
            return 1;
        }

        // Discard is the same verdict the policy reaches for something barely started: the position goes and
        // the item is not marked watched, because nobody watched it.
        if (movieId is { } film)
        {
            await _watched
                .RecordMovieProgressAsync(film, WatchOutcome.Discard, TimeSpan.Zero, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine($"Film {film} is no longer part-watched.");
        }
        else
        {
            await _watched
                .RecordEpisodeProgressAsync(episodeId!.Value, WatchOutcome.Discard, TimeSpan.Zero, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine($"Episode {episodeId} is no longer part-watched.");
        }

        return 0;
    }

    /// <summary>
    /// Opens a stored film or episode for a few seconds and releases it again.
    /// </summary>
    /// <remarks>
    /// The equivalent of <c>play-test</c> for video on demand, and it verifies two things that command
    /// cannot: that the <c>/movie/</c> and <c>/series/</c> address shapes are right for this panel, and
    /// that a resume position is honoured — a film asked to start forty minutes in that begins at zero
    /// looks perfectly healthy from every other angle.
    /// </remarks>
    /// <param name="remember">
    /// Whether to record where playback got to, as the window does. Off by default so that a play-test stays
    /// a read-only check; on, it is what lets the continue-watching list be exercised without the window.
    /// </param>
    /// <param name="seekToSeconds">
    /// Where to seek part-way through the hold, or zero for no seek. Distinct from
    /// <paramref name="startAtSeconds"/>, which is honoured while the stream is opening: that path is the
    /// resume, and this one is what the seek bar does to a stream already playing.
    /// </param>
    public async Task<int> PlayTestAsync(
        int sourceId,
        int? movieId,
        int? episodeId,
        int seconds,
        int startAtSeconds,
        int seekToSeconds,
        bool remember,
        CancellationToken cancellationToken)
    {
        if (await FindSourceAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
        {
            return 1;
        }

        if ((movieId is null) == (episodeId is null))
        {
            Console.Error.WriteLine("Pass exactly one of --movie-id and --episode-id.");
            return 1;
        }

        var startAt = startAtSeconds > 0 ? TimeSpan.FromSeconds(startAtSeconds) : (TimeSpan?)null;
        var resolver = _providers.GetStreamUrlResolver(source);
        MediaRequest request;

        if (movieId is { } film)
        {
            var movie = await _catalogue.GetMovieAsync(film, cancellationToken).ConfigureAwait(false);

            if (movie is null)
            {
                Console.Error.WriteLine($"No film with id {film}.");
                return 1;
            }

            request = resolver.ResolveMovie(source, movie, startAt);

            if (remember)
            {
                // Seeded with the position playback was asked to start at, exactly as the window does: a
                // deep seek can take longer than this command holds the stream, and reading that as "back
                // at the beginning" would throw the viewer's place away.
                _progress.Track(ContentKind.Movie, movie.Id, startAt ?? TimeSpan.Zero);
            }
        }
        else
        {
            var episode = await _catalogue.GetEpisodeAsync(episodeId!.Value, cancellationToken)
                .ConfigureAwait(false);

            if (episode is null)
            {
                Console.Error.WriteLine($"No episode with id {episodeId}.");
                return 1;
            }

            request = resolver.ResolveEpisode(source, episode, startAt);

            if (remember)
            {
                _progress.Track(ContentKind.Series, episode.Id, startAt ?? TimeSpan.Zero);
            }
        }

        var seekTo = seekToSeconds > 0 ? TimeSpan.FromSeconds(seekToSeconds) : (TimeSpan?)null;

        return await PlayAsync(source, request, seconds, seekTo, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> PlayAsync(
        PlaylistSource source,
        MediaRequest request,
        int seconds,
        TimeSpan? seekTo,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Opening '{request.DisplayName}' as {request.Format}...");

        if (request.StartAt is { } startAt)
        {
            Console.WriteLine($"Starting at {startAt:hh\\:mm\\:ss}.");
        }

        try
        {
            var state = await _session.SwitchToAsync(request, cancellationToken).ConfigureAwait(false);

            if (state != PlaybackState.Playing)
            {
                Console.Error.WriteLine($"Playback did not start; final state was {state}.");
                await ReportFailureAsync(source, cancellationToken).ConfigureAwait(false);

                return 1;
            }

            Console.WriteLine($"Playing. Holding the stream for {seconds}s.");
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);

            if (seekTo is { } target)
            {
                await SeekAndReportAsync(target, seconds, cancellationToken).ConfigureAwait(false);
            }

            // The two figures that decide whether resuming can work at all. A film reporting no duration
            // can never be recognised as finished, and one reporting no position can never be resumed.
            Console.WriteLine($"Position   {DescribeTime(_session.Position)}");
            Console.WriteLine($"Duration   {DescribeTime(_session.Duration)}");

            // Sampled before the stream is released, because the engine has neither figure afterwards — the
            // same reason the window samples on a timer rather than at the moment of saving.
            _progress.Observe(_session.Position, _session.Duration);

            if (await _progress.RecordAsync(cancellationToken).ConfigureAwait(false) is { } outcome)
            {
                Console.WriteLine($"Remembered  {outcome}");
            }
        }
        catch (PlaybackFailedException exception)
        {
            // Caught here rather than left to the runner, which has no source to ask about. The panel is the
            // only thing that knows whether this was the film, the connection limit or the subscription.
            Console.Error.WriteLine($"Playback error: {exception.Message}");
            await ReportFailureAsync(source, cancellationToken).ConfigureAwait(false);

            return 1;
        }
        finally
        {
            // Whatever was being followed is dropped if it was never recorded, so an interrupted run does
            // not leave the recorder holding an item for the next command in the same process.
            _progress.Forget();

            // Not passed the caller's token: releasing must happen even when the run was interrupted.
            await _session.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        Console.WriteLine("Stream released.");
        await _releaseCheck.ReportAsync(source, cancellationToken).ConfigureAwait(false);

        return 0;
    }

    /// <summary>
    /// Asks the panel why a stream would not open, and says so.
    /// </summary>
    private async Task ReportFailureAsync(PlaylistSource source, CancellationToken cancellationToken)
    {
        var reason = await _failures.ExplainAsync(source, cancellationToken).ConfigureAwait(false);

        Console.Error.WriteLine($"Reason:  {reason}");
        Console.Error.WriteLine($"         {StreamFailureNotes.Describe(reason)}");
    }

    /// <summary>
    /// Moves a playing stream and reports where it ended up.
    /// </summary>
    /// <remarks>
    /// Held again afterwards, because a seek over HTTP is answered by a fresh range request and the engine
    /// reports the old position until that arrives. Reading immediately would say the seek did nothing on
    /// exactly the panels where it takes longest — which is the case worth knowing about.
    /// </remarks>
    private async Task SeekAndReportAsync(TimeSpan target, int seconds, CancellationToken cancellationToken)
    {
        if (!_session.IsSeekable)
        {
            Console.Error.WriteLine(
                "Seek       refused; the panel serves this without range support, so it cannot be "
                + "positioned at all.");
            return;
        }

        Console.WriteLine($"Seeking to {target:hh\\:mm\\:ss}, then holding another {seconds}s.");
        _session.SeekTo(target);

        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Sought to  {DescribeTime(_session.Position)}");
    }

    /// <summary>
    /// Explains an empty section, which is otherwise indistinguishable from a subscription that has none.
    /// </summary>
    private static void ReportSectionState(PlaylistSource source, int storedCount, ContentKind kind)
    {
        if (storedCount > 0)
        {
            return;
        }

        var supported = kind == ContentKind.Movie
            ? source.Capabilities.SupportsVod
            : source.Capabilities.SupportsSeries;

        Console.WriteLine(
            supported
                ? "The panel was probed as offering this section, so an empty one means the import found "
                    + "nothing in it. Refresh the source to try again."
                : "The panel was probed as not offering this section, so nothing was fetched. That is not "
                    + "a fault: many subscriptions sell live television only.");
    }

    private static List<T> Narrow<T>(IReadOnlyList<T> items, string? filter, Func<T, string> nameOf)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return [.. items];
        }

        var criteria = new CatalogueFilter(SearchText: filter);
        return [.. items.Where(item => criteria.Matches(nameOf(item), categoryExternalId: null))];
    }

    private static int Positive(int limit)
    {
        return limit > 0 ? limit : DefaultLimit;
    }

    private static string DescribeKind(ContentKind kind)
    {
        return kind == ContentKind.Movie ? "film" : "episode";
    }

    private static string DescribeEntry(ContinueWatchingEntry entry)
    {
        return string.IsNullOrEmpty(entry.Subtitle) ? entry.Title : $"{entry.Title} · {entry.Subtitle}";
    }

    private static string DescribeResume(int? resumePositionSeconds, bool isWatched)
    {
        if (resumePositionSeconds is { } seconds)
        {
            return $"at {DescribeDuration(seconds)}";
        }

        return isWatched ? "watched" : "-";
    }

    private static string DescribeDuration(int? seconds)
    {
        return seconds is > 0 ? DescribeTime(TimeSpan.FromSeconds(seconds.Value)) : "-";
    }

    private static string DescribeTime(TimeSpan? value)
    {
        return value is { } time ? $"{time:hh\\:mm\\:ss}" : "unknown";
    }

    private async Task<PlaylistSource?> FindSourceAsync(int sourceId, CancellationToken cancellationToken)
    {
        var sources = await _sources.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        var source = sources.FirstOrDefault(candidate => candidate.Id == sourceId);

        if (source is null)
        {
            Console.Error.WriteLine($"No source with id {sourceId}. Run 'sources list' to see what there is.");
        }

        return source;
    }
}
