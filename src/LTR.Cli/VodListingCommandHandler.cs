using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Cli;

/// <summary>
/// Lists what a stored source holds in films and in series.
/// </summary>
/// <remarks>
/// The figures worth reading are the ones an import can get wrong while appearing to succeed. A film count
/// of zero on a subscription that sells films means the capability probe said no, which is why an empty
/// section explains itself rather than printing nothing.
/// </remarks>
internal sealed class VodListingCommandHandler
{
    private readonly StoredSourceLookup _sources;
    private readonly IVodCatalogue _catalogue;

    public VodListingCommandHandler(StoredSourceLookup sources, IVodCatalogue catalogue)
    {
        _sources = sources;
        _catalogue = catalogue;
    }

    public async Task<int> ListMoviesAsync(
        int sourceId,
        string? filter,
        int limit,
        CancellationToken cancellationToken)
    {
        if (await _sources.FindAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
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
                + $"{Year(movie.Year),-6} "
                + $"{movie.ContainerExtension ?? "-",-6} "
                + $"{VodText.Resume(movie.ResumePositionSeconds, movie.IsWatched)}");
        }

        return 0;
    }

    public async Task<int> ListSeriesAsync(
        int sourceId,
        string? filter,
        int limit,
        CancellationToken cancellationToken)
    {
        if (await _sources.FindAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
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
                + $"{Year(item.Year),-6} "
                + $"{ConsoleText.FormatUtc(item.DetailFetchedUtc)}");
        }

        return 0;
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

    /// <remarks>
    /// Filtered through <see cref="CatalogueFilter"/> rather than with a bare <c>Contains</c>, so the command
    /// line matches what the window matches.
    /// </remarks>
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
        return limit > 0 ? limit : Commands.CommandDefaults.Limit;
    }

    private static string Year(int? year)
    {
        return year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";
    }
}
