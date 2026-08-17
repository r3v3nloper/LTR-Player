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
    private readonly IPlaybackTransport _playback;
    private readonly StreamHoldTest _holdTest;
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
        IPlaybackTransport playback,
        StreamHoldTest holdTest,
        WatchProgressRecorder progress)
    {
        _sources = sources;
        _catalogue = catalogue;
        _watched = watched;
        _detail = detail;
        _providers = providers;
        _playback = playback;
        _holdTest = holdTest;
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
        Console.WriteLine($"Detail     {DescribeDetailState(movie)}");
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

        // Its own operation rather than a discarding outcome: the position goes, the item is not marked
        // watched because nobody watched it, and nothing is recorded about when — which a watch outcome
        // cannot express, since every one of them states a moment.
        if (movieId is { } film)
        {
            await _watched.ForgetMovieProgressAsync(film, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Film {film} is no longer part-watched.");
        }
        else
        {
            await _watched
                .ForgetEpisodeProgressAsync(episodeId!.Value, cancellationToken)
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

    /// <summary>
    /// Holds the stream through the shared play-test, adding the parts only a film needs.
    /// </summary>
    private async Task<int> PlayAsync(
        PlaylistSource source,
        MediaRequest request,
        int seconds,
        TimeSpan? seekTo,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _holdTest
                .RunAsync(
                    source,
                    request,
                    seconds,
                    token => ReportPlaybackAsync(seekTo, seconds, token),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // Whatever was being followed is dropped if it was never recorded, so an interrupted run does
            // not leave the recorder holding an item for the next command in the same process.
            _progress.Forget();
        }
    }

    /// <summary>
    /// Reads what only a playing film can report, and records where it got to.
    /// </summary>
    /// <remarks>
    /// Runs while the stream is still open, because the engine has neither figure afterwards — the same
    /// reason the window samples on a timer rather than at the moment of saving.
    /// </remarks>
    private async Task ReportPlaybackAsync(TimeSpan? seekTo, int seconds, CancellationToken cancellationToken)
    {
        if (seekTo is { } target)
        {
            await SeekAndReportAsync(target, seconds, cancellationToken).ConfigureAwait(false);
        }

        // The two figures that decide whether resuming can work at all. A film reporting no duration
        // can never be recognised as finished, and one reporting no position can never be resumed.
        Console.WriteLine($"Position   {DescribeTime(_playback.Position)}");
        Console.WriteLine($"Duration   {DescribeTime(_playback.Duration)}");

        _progress.Observe(_playback.Position, _playback.Duration);

        if (await _progress.RecordAsync(cancellationToken).ConfigureAwait(false) is { } outcome)
        {
            Console.WriteLine($"Remembered  {outcome}");
        }
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
        if (!_playback.IsSeekable)
        {
            Console.Error.WriteLine(
                "Seek       refused; the panel serves this without range support, so it cannot be "
                + "positioned at all.");
            return;
        }

        Console.WriteLine($"Seeking to {target:hh\\:mm\\:ss}, then holding another {seconds}s.");
        _playback.SeekTo(target);

        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Sought to  {DescribeTime(_playback.Position)}");
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

    /// <summary>
    /// Says whether a film's detail is stored and, when it is not, when the panel was last asked.
    /// </summary>
    /// <remarks>
    /// The asking is worth printing because it is what decides whether opening the film costs a request:
    /// a panel that answers with nothing is taken at its word for a day. Without this line, "not available"
    /// looks identical whether it has been asked once or on every viewing since the catalogue was imported.
    /// </remarks>
    private static string DescribeDetailState(VodItem movie)
    {
        if (movie.HasDetail)
        {
            return "fetched";
        }

        return movie.DetailAttemptedUtc is { } attempted
            ? $"not available (asked {ConsoleText.FormatUtc(attempted)})"
            : "not available (never asked)";
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
