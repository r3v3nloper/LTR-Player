using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.EntityFrameworkCore;

namespace LTR.Persistence;

/// <summary>
/// How a film and series catalogue is seeded into real SQLite, for the tests that read one back.
/// </summary>
/// <remarks>
/// Shared rather than duplicated because both halves of the split need nearly all of it: the film and
/// series tests seed a catalogue to reconcile, the watch-progress tests seed one to record a position
/// against. Reached through <c>using static</c>, so a call site reads as it did when these lived in the one
/// file they came out of.
/// </remarks>
internal static class VodFixtures
{
    /// <summary>A fixed instant, so a test can place a viewing around a known moment.</summary>
    public static readonly DateTimeOffset SixPm = new(2026, 8, 12, 18, 0, 0, TimeSpan.Zero);

    public static async Task<int> AddSourceAsync(
        SqliteTestDatabase database,
        CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext();

        return await context.AddSourceAsync(
            new XtreamSource
            {
                Name = "Test source",
                BaseUrl = new Uri("http://panel.example:8080", UriKind.Absolute),
                Username = "alice",
                Password = "pass",
                CreatedUtc = SixPm,
            },
            cancellationToken);
    }

    public static async Task<int> StoreOneMovieAsync(
        SqliteTestDatabase database,
        int sourceId,
        CancellationToken cancellationToken)
    {
        return (await StoreCatalogueAsync(database, sourceId, withMovie: true, withSeries: false, cancellationToken))
            .MovieId;
    }

    public static async Task<int> StoreOneSeriesAsync(
        SqliteTestDatabase database,
        int sourceId,
        DateTimeOffset lastModifiedUtc,
        CancellationToken cancellationToken)
    {
        return (await StoreCatalogueAsync(
                database,
                sourceId,
                withMovie: false,
                withSeries: true,
                cancellationToken,
                lastModifiedUtc))
            .SeriesId;
    }

    /// <summary>
    /// Stores a catalogue in one pass.
    /// </summary>
    /// <remarks>
    /// One call rather than one per kind, because a reconciliation is authoritative: storing a film and
    /// then storing a series with an empty film list correctly deletes the film again.
    /// </remarks>
    public static async Task<(int MovieId, int SeriesId)> StoreCatalogueAsync(
        SqliteTestDatabase database,
        int sourceId,
        bool withMovie,
        bool withSeries,
        CancellationToken cancellationToken,
        DateTimeOffset? lastModifiedUtc = null)
    {
        await using var context = database.CreateContext();

        var series = SeriesEntry("4321", "Breaking Bad");
        series.LastModifiedUtc = lastModifiedUtc ?? SixPm;

        await context.ReconcileVodCatalogueAsync(
            sourceId,
            [],
            withMovie ? [Movie("1", "Arrival")] : [],
            withSeries ? [series] : [],
            cancellationToken);

        var movies = await context.GetMoviesAsync(sourceId, cancellationToken);
        var stored = await context.GetSeriesAsync(sourceId, cancellationToken);

        return (
            withMovie ? movies.Single().Id : 0,
            withSeries ? stored.Single().Id : 0);
    }

    public static async Task<int> EpisodeIdAsync(
        SqliteTestDatabase database,
        string externalId,
        CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext();

        return await context.Episodes
            .Where(episode => episode.ExternalId == externalId)
            .Select(episode => episode.Id)
            .SingleAsync(cancellationToken);
    }

    public static async Task<int> SingleEpisodeIdAsync(
        SqliteTestDatabase database,
        CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext();
        return await context.Episodes.Select(episode => episode.Id).SingleAsync(cancellationToken);
    }

    public static async Task<Dictionary<string, int>> MovieIdsByNameAsync(
        SqliteTestDatabase database,
        int sourceId,
        CancellationToken cancellationToken)
    {
        await using var context = database.CreateContext();

        return (await context.GetMoviesAsync(sourceId, cancellationToken))
            .ToDictionary(movie => movie.Name, movie => movie.Id);
    }

    public static Category Category(string externalId, string name, ContentKind kind)
    {
        return new Category { ExternalId = externalId, Name = name, Kind = kind };
    }

    public static Channel Channel(string externalId, string name, string? categoryExternalId = null)
    {
        return new Channel
        {
            ExternalId = externalId,
            Name = name,
            CategoryExternalId = categoryExternalId,
        };
    }

    public static VodItem Movie(string externalId, string name, string? categoryExternalId = null)
    {
        return new VodItem
        {
            ExternalId = externalId,
            Name = name,
            CategoryExternalId = categoryExternalId,
        };
    }

    public static Series SeriesEntry(string externalId, string name, string? categoryExternalId = null)
    {
        return new Series
        {
            ExternalId = externalId,
            Name = name,
            CategoryExternalId = categoryExternalId,
        };
    }

    public static Season SeasonWith(int number, params Episode[] episodes)
    {
        return new Season { Number = number, Episodes = [.. episodes] };
    }

    public static Episode Episode(string externalId, string title, int number)
    {
        return new Episode
        {
            ExternalId = externalId,
            Title = title,
            Number = number,
            ContainerExtension = "mkv",
        };
    }
}
