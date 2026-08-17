using LTR.Catalogue;
using LTR.Core.Content;

namespace LTR.Cli;

/// <summary>
/// Shows one film or one series, fetching its detail from the panel if the stored copy will not do.
/// </summary>
internal sealed class VodDetailCommandHandler
{
    private readonly StoredSourceLookup _sources;
    private readonly IVodDetailService _detail;

    public VodDetailCommandHandler(StoredSourceLookup sources, IVodDetailService detail)
    {
        _sources = sources;
        _detail = detail;
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
        if (await _sources.FindAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
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
        Console.WriteLine(
            $"Provider   id {series.ExternalId}, changed {ConsoleText.FormatUtc(series.LastModifiedUtc)}");
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
            PrintSeason(season);
        }

        return 0;
    }

    public async Task<int> ShowMovieAsync(int sourceId, int movieId, CancellationToken cancellationToken)
    {
        if (await _sources.FindAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
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
        Console.WriteLine(
            $"Provider   id {movie.ExternalId}, container {movie.ContainerExtension ?? "unstated"}");
        Console.WriteLine(
            $"Year       {movie.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}");
        Console.WriteLine($"Runtime    {VodText.Duration(movie.DurationSeconds)}");
        Console.WriteLine($"Detail     {VodText.DetailState(movie)}");
        Console.WriteLine($"Resume     {VodText.Resume(movie.ResumePositionSeconds, movie.IsWatched)}");

        if (!string.IsNullOrWhiteSpace(movie.Plot))
        {
            Console.WriteLine();
            Console.WriteLine(movie.Plot);
        }

        return 0;
    }

    private static void PrintSeason(Season season)
    {
        Console.WriteLine();
        Console.WriteLine($"  Season {season.Number} — {season.Episodes.Count} episodes");

        foreach (var episode in season.Episodes)
        {
            Console.WriteLine(
                $"    {episode.Id,-6} {EpisodeNaming.Label(season.Number, episode.Number),-8} "
                + $"{ConsoleText.Truncate(episode.Title, 40),-40} {episode.ContainerExtension ?? "-",-5} "
                + $"{VodText.Resume(episode.ResumePositionSeconds, episode.IsWatched)}");
        }
    }
}
