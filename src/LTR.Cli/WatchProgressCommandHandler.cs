using LTR.Catalogue;

namespace LTR.Cli;

/// <summary>
/// The continue-watching list, and taking something off it.
/// </summary>
/// <remarks>
/// Together because they are the two halves of one loop, and that loop is what makes resuming verifiable
/// without the window: play with <c>--remember</c>, see it listed here, take it off again.
/// </remarks>
internal sealed class WatchProgressCommandHandler
{
    private readonly StoredSourceLookup _sources;
    private readonly IWatchProgressStore _watched;

    public WatchProgressCommandHandler(StoredSourceLookup sources, IWatchProgressStore watched)
    {
        _sources = sources;
        _watched = watched;
    }

    public async Task<int> ContinueWatchingAsync(int sourceId, CancellationToken cancellationToken)
    {
        if (await _sources.FindAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
        {
            return 1;
        }

        var entries = await _watched
            .GetContinueWatchingAsync(source.Id, Commands.CommandDefaults.Limit, cancellationToken)
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
                $"{VodText.Kind(entry.Kind),-7} {entry.ItemId,-6} "
                + $"{ConsoleText.Truncate(VodText.Entry(entry), 38),-38} "
                + $"{VodText.Duration(entry.PositionSeconds),-9} "
                + $"{VodText.Duration(entry.DurationSeconds),-9} "
                + $"{ConsoleText.FormatUtc(entry.LastWatchedUtc)}");
        }

        return 0;
    }

    /// <summary>
    /// Forgets where the viewer got to, taking an item off the continue-watching list.
    /// </summary>
    /// <remarks>
    /// The command-line counterpart of the list's own remove button.
    /// </remarks>
    public async Task<int> ForgetAsync(
        int sourceId,
        int? movieId,
        int? episodeId,
        CancellationToken cancellationToken)
    {
        if (await _sources.FindAsync(sourceId, cancellationToken).ConfigureAwait(false) is null)
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
}
