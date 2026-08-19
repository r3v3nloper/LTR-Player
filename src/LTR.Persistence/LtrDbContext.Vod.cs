using LTR.Core.Content;
using LTR.Core.Playback;
using Microsoft.EntityFrameworkCore;

namespace LTR.Persistence;

/// <summary>
/// The film and series half of the unit of work: the schema, the reconciliation of a catalogue, and the
/// cached season detail.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the live catalogue because two things about it are different in kind. A series is
/// stored in two passes rather than one — a shallow listing during an import, its seasons only when it
/// is opened — and a film carries the viewer's own position, which every write here has to preserve as
/// carefully as the live half preserves a favourite. Reading and writing that position is
/// <c>LtrDbContext.WatchProgress.cs</c>, which is the face <c>IWatchProgressStore</c> draws over this
/// context from the application layer; what is here is everything a provider owns.
/// </para>
/// <para>
/// One rule runs through all of it: a listing may overwrite what a listing owns, but must never blank out
/// what a detail call supplied. Panels state a synopsis in the detail response and not in the listing, so
/// a refresh that assigned the listing's fields unconditionally would erase every synopsis the player had
/// fetched.
/// </para>
/// </remarks>
public sealed partial class LtrDbContext
{
    /// <summary>
    /// Escape character for <c>LIKE</c> patterns. A backslash, because it is the one character a film
    /// title never contains while <c>%</c>, <c>_</c> and <c>!</c> all do.
    /// </summary>
    private const string LikeEscapeCharacter = "\\";

    /// <summary>
    /// Reconciles a freshly fetched film and series catalogue against what is stored.
    /// </summary>
    /// <param name="categories">
    /// The film and series categories together, since both kinds are fetched in one import and the
    /// reconciliation is authoritative for exactly the kinds it is given.
    /// </param>
    /// <remarks>
    /// Entries the provider no longer offers are removed, taking their stored position with them — the
    /// same trade the live catalogue makes with favourites. A film that leaves a subscription and returns
    /// is a new film here, and remembering a position into a film nobody can play would be worse.
    /// </remarks>
    public async Task ReconcileVodCatalogueAsync(
        int sourceId,
        IReadOnlyList<Category> categories,
        IReadOnlyList<VodItem> movies,
        IReadOnlyList<Series> series,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(movies);
        ArgumentNullException.ThrowIfNull(series);

        var categoryIds = await ReconcileCategoriesAsync(
                sourceId,
                categories,
                [ContentKind.Movie, ContentKind.Series],
                cancellationToken)
            .ConfigureAwait(false);

        await ReconcileMoviesAsync(sourceId, movies, categoryIds, cancellationToken).ConfigureAwait(false);
        await ReconcileSeriesAsync(sourceId, series, categoryIds, cancellationToken).ConfigureAwait(false);

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a film's detail response and records that it has been read.
    /// </summary>
    /// <remarks>
    /// The flag matters more than the fields: without it the detail call would be made again every time
    /// the film is opened, and a panel that has nothing to say about a film says so just as slowly as one
    /// that has.
    /// </remarks>
    public async Task SaveMovieDetailAsync(
        int movieId,
        MovieDetail detail,
        DateTimeOffset attemptedUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var movie = await Movies.FirstOrDefaultAsync(item => item.Id == movieId, cancellationToken)
            .ConfigureAwait(false);

        if (movie is null)
        {
            return;
        }

        movie.Plot = detail.Plot ?? movie.Plot;
        movie.Genre = detail.Genre ?? movie.Genre;
        movie.Cast = detail.Cast ?? movie.Cast;
        movie.Director = detail.Director ?? movie.Director;
        movie.Year = detail.Year ?? movie.Year;
        movie.Rating = detail.Rating ?? movie.Rating;
        movie.DurationSeconds = detail.DurationSeconds ?? movie.DurationSeconds;
        movie.ContainerExtension = detail.ContainerExtension ?? movie.ContainerExtension;
        movie.HasDetail = true;
        movie.DetailAttemptedUtc = attemptedUtc;

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records that the provider was asked for a film's detail and had none, without claiming one arrived.
    /// </summary>
    /// <remarks>
    /// This is what stops the same film being asked about on every viewing. Deliberately not
    /// <c>HasDetail = true</c>, which would say a synopsis was stored and stop it ever being asked for
    /// again: panels do fill their detail in over time, so the answer is taken at its word for
    /// <see cref="VodItem.DetailRetryInterval"/> and no longer.
    /// </remarks>
    public Task RecordMovieDetailAbsentAsync(
        int movieId,
        DateTimeOffset attemptedUtc,
        CancellationToken cancellationToken)
    {
        return Movies
            .Where(movie => movie.Id == movieId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(movie => movie.DetailAttemptedUtc, attemptedUtc),
                cancellationToken);
    }

    /// <summary>
    /// Stores a series' seasons and episodes, and returns how many episodes it now holds.
    /// </summary>
    /// <param name="providerModifiedUtc">
    /// The <c>last_modified</c> value the provider reported for the series when this detail was fetched.
    /// Recorded so a later listing that moves it is what triggers a re-fetch — a series nobody has changed
    /// is never fetched twice, however old the stored copy is.
    /// </param>
    /// <remarks>
    /// A reconciliation, not a replacement. Episodes are matched by their own identifier across the whole
    /// series rather than within one season, so an episode the provider refiles into another season keeps
    /// the position the viewer reached in it instead of coming back unwatched.
    /// </remarks>
    public async Task<int> SaveSeriesDetailAsync(
        int seriesId,
        SeriesDetail detail,
        DateTimeOffset providerModifiedUtc,
        DateTimeOffset fetchedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(detail);

        // Split, for the same reason the read path is: one query per collection instead of a join that
        // repeats the series' own synopsis — several kilobytes of it — on each of a dozen seasons' worth of
        // episodes. EF warns about the joined form on every fetch.
        //
        // The usual caveat about split queries does not bite here. They can observe data changed between the
        // two statements, but this is the write path of a single-writer application: the guide import does not
        // touch series, and two series fetches cannot run at once because one viewer opens one series.
        var stored = await Series
            .AsSplitQuery()
            .Include(item => item.Seasons)
            .ThenInclude(season => season.Episodes)
            .FirstOrDefaultAsync(item => item.Id == seriesId, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null)
        {
            return 0;
        }

        stored.Plot = detail.Plot ?? stored.Plot;
        stored.Genre = detail.Genre ?? stored.Genre;
        stored.Cast = detail.Cast ?? stored.Cast;
        stored.Director = detail.Director ?? stored.Director;
        stored.Year = detail.Year ?? stored.Year;
        stored.Rating = detail.Rating ?? stored.Rating;

        // The algorithm itself is in Core: it works on entities already in hand and performs no I/O, and
        // living here meant real SQLite was the only way to test the one thing it is subtle about.
        var episodeCount = SeriesReconciliation.Apply(stored, detail);

        stored.DetailFetchedUtc = fetchedAtUtc;
        stored.DetailModifiedUtc = providerModifiedUtc;

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return episodeCount;
    }

    /// <summary>Loads a source's films, ordered the way the provider intended.</summary>
    public async Task<IReadOnlyList<VodItem>> GetMoviesAsync(int sourceId, CancellationToken cancellationToken)
    {
        return await Movies
            .AsNoTracking()
            .Where(movie => movie.SourceId == sourceId)
            .OrderBy(movie => movie.SortOrder)
            .ThenBy(movie => movie.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Loads a source's series, without their seasons.</summary>
    public async Task<IReadOnlyList<Series>> GetSeriesAsync(int sourceId, CancellationToken cancellationToken)
    {
        return await Series
            .AsNoTracking()
            .Where(series => series.SourceId == sourceId)
            .OrderBy(series => series.SortOrder)
            .ThenBy(series => series.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Answers a search over a source's films, bounded by <paramref name="limit"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Filtered and counted by the database rather than in memory, unlike the channel list. The
    /// subscription this was built against holds sixty-six thousand films — four times its channel count —
    /// and nobody browses that by scrolling, so the list answers a search instead of presenting the whole
    /// catalogue. Wrapping every one of them in a row object to filter them again afterwards would cost
    /// seconds and a great deal of memory to display twenty.
    /// </para>
    /// <para>
    /// The name criterion is <c>LIKE</c>, which SQLite applies case-insensitively to ASCII. That is a near
    /// match for the in-memory <see cref="CatalogueFilter"/> rather than an exact one: accented letters
    /// compare case-sensitively here and not there. The near match is deliberate — the alternative is
    /// reading the whole table to apply the rule exactly.
    /// </para>
    /// </remarks>
    public async Task<CataloguePage<VodItem>> SearchMoviesAsync(
        int sourceId,
        string? searchText,
        string? categoryExternalId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var query = Movies.AsNoTracking().Where(movie => movie.SourceId == sourceId);

        if (!string.IsNullOrWhiteSpace(categoryExternalId))
        {
            query = query.Where(movie => movie.CategoryExternalId == categoryExternalId);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var pattern = LikePattern(searchText);
            query = query.Where(movie => EF.Functions.Like(movie.Name, pattern, LikeEscapeCharacter));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderBy(movie => movie.SortOrder)
            .ThenBy(movie => movie.Name)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CataloguePage<VodItem>(items, total);
    }

    /// <summary>Answers a search over a source's series, bounded the same way as the film search.</summary>
    public async Task<CataloguePage<Series>> SearchSeriesAsync(
        int sourceId,
        string? searchText,
        string? categoryExternalId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var query = Series.AsNoTracking().Where(series => series.SourceId == sourceId);

        if (!string.IsNullOrWhiteSpace(categoryExternalId))
        {
            query = query.Where(series => series.CategoryExternalId == categoryExternalId);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            var pattern = LikePattern(searchText);
            query = query.Where(series => EF.Functions.Like(series.Name, pattern, LikeEscapeCharacter));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderBy(series => series.SortOrder)
            .ThenBy(series => series.Name)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CataloguePage<Series>(items, total);
    }

    /// <summary>
    /// Turns a user's search text into a <c>LIKE</c> pattern that means what they typed.
    /// </summary>
    /// <remarks>
    /// The wildcards have to be escaped: a viewer typing <c>%</c> means a percent sign, and left alone it
    /// would match the entire catalogue. The escape character itself is escaped first, or escaping the
    /// wildcards would corrupt it.
    /// </remarks>
    private static string LikePattern(string searchText)
    {
        var escaped = searchText
            .Trim()
            .Replace(LikeEscapeCharacter, LikeEscapeCharacter + LikeEscapeCharacter, StringComparison.Ordinal)
            .Replace("%", LikeEscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscapeCharacter + "_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }

    /// <summary>
    /// Loads one series with its seasons and episodes in order, or <see langword="null"/> when it is gone.
    /// </summary>
    /// <remarks>
    /// Split queries, because a series with a dozen seasons of twenty episodes each would otherwise repeat
    /// its own synopsis — several kilobytes of it — on every one of two hundred and forty rows.
    /// </remarks>
    public Task<Series?> GetSeriesDetailAsync(int seriesId, CancellationToken cancellationToken)
    {
        return Series
            .AsNoTracking()
            .AsSplitQuery()
            .Include(series => series.Seasons.OrderBy(season => season.Number))
            .ThenInclude(season => season.Episodes.OrderBy(episode => episode.Number))
            .FirstOrDefaultAsync(series => series.Id == seriesId, cancellationToken);
    }

    /// <summary>Loads one film, or <see langword="null"/> when it is no longer in the catalogue.</summary>
    public Task<VodItem?> GetMovieAsync(int movieId, CancellationToken cancellationToken)
    {
        return Movies.AsNoTracking().FirstOrDefaultAsync(movie => movie.Id == movieId, cancellationToken);
    }

    /// <summary>Loads one episode, or <see langword="null"/> when it is no longer in the catalogue.</summary>
    public Task<Episode?> GetEpisodeAsync(int episodeId, CancellationToken cancellationToken)
    {
        return Episodes
            .AsNoTracking()
            .FirstOrDefaultAsync(episode => episode.Id == episodeId, cancellationToken);
    }

    /// <summary>
    /// Loads the series one episode belongs to, with its seasons and episodes in order.
    /// </summary>
    /// <remarks>
    /// Two queries rather than one, and deliberately so: the identifier is looked up on its own and the
    /// series then loaded by <see cref="GetSeriesDetailAsync"/>, which already states how a series is loaded
    /// whole. Reaching the seasons through the episode's own navigation would be a third spelling of that.
    /// </remarks>
    public async Task<Series?> GetSeriesForEpisodeAsync(int episodeId, CancellationToken cancellationToken)
    {
        var seriesId = await Episodes
            .AsNoTracking()
            .Where(episode => episode.Id == episodeId)
            .Select(episode => (int?)episode.Season!.SeriesId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return seriesId is { } id
            ? await GetSeriesDetailAsync(id, cancellationToken).ConfigureAwait(false)
            : null;
    }

    private async Task ReconcileMoviesAsync(
        int sourceId,
        IReadOnlyList<VodItem> incoming,
        Dictionary<(string ExternalId, ContentKind Kind), int> categoryIds,
        CancellationToken cancellationToken)
    {
        var existing = await Movies
            .Where(movie => movie.SourceId == sourceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var movie in incoming)
        {
            movie.CategoryId = ResolveCategoryId(movie.CategoryExternalId, ContentKind.Movie, categoryIds);
        }

        var reconciliation = CatalogueReconciler.Match(
            existing,
            incoming,
            movie => movie.ExternalId,
            StringComparer.Ordinal);

        // Which fields a listing may assign and which it must leave alone is stated on VodItem, because it is
        // a fact about a film and not about this table.
        foreach (var (stored, fetched) in reconciliation.Matched)
        {
            stored.AdoptListingFields(fetched);
        }

        foreach (var movie in reconciliation.Added)
        {
            movie.SourceId = sourceId;
            Movies.Add(movie);
        }

        Movies.RemoveRange(reconciliation.Removed);
    }

    private async Task ReconcileSeriesAsync(
        int sourceId,
        IReadOnlyList<Series> incoming,
        Dictionary<(string ExternalId, ContentKind Kind), int> categoryIds,
        CancellationToken cancellationToken)
    {
        var existing = await Series
            .Where(series => series.SourceId == sourceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var series in incoming)
        {
            series.CategoryId = ResolveCategoryId(series.CategoryExternalId, ContentKind.Series, categoryIds);
        }

        var reconciliation = CatalogueReconciler.Match(
            existing,
            incoming,
            series => series.ExternalId,
            StringComparer.Ordinal);

        foreach (var (stored, fetched) in reconciliation.Matched)
        {
            stored.AdoptListingFields(fetched);
        }

        foreach (var series in reconciliation.Added)
        {
            series.SourceId = sourceId;
            Series.Add(series);
        }

        Series.RemoveRange(reconciliation.Removed);
    }

    private static void ConfigureVod(ModelBuilder modelBuilder)
    {
        var movie = modelBuilder.Entity<VodItem>();

        movie.ToTable("Movies");
        movie.HasKey(entity => entity.Id);
        movie.Property(entity => entity.ExternalId).IsRequired().HasMaxLength(400);
        movie.Property(entity => entity.Name).IsRequired().HasMaxLength(600);
        movie.Property(entity => entity.CoverUrl).HasMaxLength(2000);
        movie.Property(entity => entity.ContainerExtension).HasMaxLength(20);
        movie.Property(entity => entity.CategoryExternalId).HasMaxLength(200);
        movie.Property(entity => entity.Plot).HasMaxLength(4000);
        movie.Property(entity => entity.Genre).HasMaxLength(400);
        movie.Property(entity => entity.Cast).HasMaxLength(2000);
        movie.Property(entity => entity.Director).HasMaxLength(400);

        // Every instant goes through the UTC converter for the reason stated on it: the continue-watching
        // list orders by the time last watched, and EF's default DateTimeOffset mapping cannot be ordered
        // by in SQLite at all. DetailAttemptedUtc is only ever compared in memory, and is converted anyway
        // rather than left as the one column that would break the day something filters on it.
        movie.Property(entity => entity.AddedUtc).HasConversion(NullableUtcInstantConverter);
        movie.Property(entity => entity.LastWatchedUtc).HasConversion(NullableUtcInstantConverter);
        movie.Property(entity => entity.DetailAttemptedUtc).HasConversion(NullableUtcInstantConverter);

        movie.Ignore(entity => entity.Duration);

        movie.HasIndex(entity => new { entity.SourceId, entity.ExternalId }).IsUnique();

        // The continue-watching list asks for one source's part-watched films ordered by when they were
        // watched, and would otherwise scan a table with tens of thousands of rows in it.
        movie.HasIndex(entity => new { entity.SourceId, entity.LastWatchedUtc });

        movie.HasOne(entity => entity.Source)
            .WithMany()
            .HasForeignKey(entity => entity.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        // As for channels: a category disappearing provider-side leaves its films uncategorised rather
        // than deleting them.
        movie.HasOne(entity => entity.Category)
            .WithMany()
            .HasForeignKey(entity => entity.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        var series = modelBuilder.Entity<Series>();

        series.ToTable("Series");
        series.HasKey(entity => entity.Id);
        series.Property(entity => entity.ExternalId).IsRequired().HasMaxLength(400);
        series.Property(entity => entity.Name).IsRequired().HasMaxLength(600);
        series.Property(entity => entity.CoverUrl).HasMaxLength(2000);
        series.Property(entity => entity.CategoryExternalId).HasMaxLength(200);
        series.Property(entity => entity.Plot).HasMaxLength(4000);
        series.Property(entity => entity.Genre).HasMaxLength(400);
        series.Property(entity => entity.Cast).HasMaxLength(2000);
        series.Property(entity => entity.Director).HasMaxLength(400);
        series.Property(entity => entity.LastModifiedUtc).HasConversion(NullableUtcInstantConverter);
        series.Property(entity => entity.DetailFetchedUtc).HasConversion(NullableUtcInstantConverter);
        series.Property(entity => entity.DetailModifiedUtc).HasConversion(NullableUtcInstantConverter);
        series.Ignore(entity => entity.HasCurrentDetail);

        series.HasIndex(entity => new { entity.SourceId, entity.ExternalId }).IsUnique();

        series.HasOne(entity => entity.Source)
            .WithMany()
            .HasForeignKey(entity => entity.SourceId)
            .OnDelete(DeleteBehavior.Cascade);

        series.HasOne(entity => entity.Category)
            .WithMany()
            .HasForeignKey(entity => entity.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        var season = modelBuilder.Entity<Season>();

        season.ToTable("Seasons");
        season.HasKey(entity => entity.Id);
        season.Property(entity => entity.Name).HasMaxLength(400);
        season.Property(entity => entity.CoverUrl).HasMaxLength(2000);
        season.Property(entity => entity.Plot).HasMaxLength(4000);

        season.HasIndex(entity => new { entity.SeriesId, entity.Number }).IsUnique();

        season.HasOne(entity => entity.Series)
            .WithMany(entity => entity.Seasons)
            .HasForeignKey(entity => entity.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        var episode = modelBuilder.Entity<Episode>();

        episode.ToTable("Episodes");
        episode.HasKey(entity => entity.Id);
        episode.Property(entity => entity.ExternalId).IsRequired().HasMaxLength(400);
        episode.Property(entity => entity.Title).IsRequired().HasMaxLength(600);
        episode.Property(entity => entity.ContainerExtension).HasMaxLength(20);
        episode.Property(entity => entity.Plot).HasMaxLength(4000);
        episode.Property(entity => entity.StillUrl).HasMaxLength(2000);
        episode.Property(entity => entity.AddedUtc).HasConversion(NullableUtcInstantConverter);
        episode.Property(entity => entity.LastWatchedUtc).HasConversion(NullableUtcInstantConverter);
        episode.Ignore(entity => entity.Duration);

        // Unique within its season rather than within its series, because that is the relationship the
        // schema has. Matching across the series is done in memory, where an episode being refiled can be
        // recognised as a move.
        episode.HasIndex(entity => new { entity.SeasonId, entity.ExternalId }).IsUnique();

        episode.HasIndex(entity => entity.LastWatchedUtc);

        episode.HasOne(entity => entity.Season)
            .WithMany(entity => entity.Episodes)
            .HasForeignKey(entity => entity.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
