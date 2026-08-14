using LTR.Core.Content;
using LTR.Core.Playback;
using Microsoft.EntityFrameworkCore;

namespace LTR.Persistence;

/// <summary>
/// The film and series half of the unit of work: the schema, the reconciliation of a catalogue, the
/// cached season detail, and where the viewer left off.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the live catalogue because two things about it are different in kind. A series is
/// stored in two passes rather than one — a shallow listing during an import, its seasons only when it
/// is opened — and a film carries the viewer's own position, which every write here has to preserve as
/// carefully as the live half preserves a favourite.
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

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

        var stored = await Series
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

        var episodeCount = ReconcileSeasons(stored, detail);

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
    /// Lists what the viewer is part-way through, most recently watched first.
    /// </summary>
    /// <remarks>
    /// Two queries and a merge rather than one union. Films and episodes share no table, and the four
    /// fields each contributes are reached differently — an episode's title and cover come from its series
    /// two joins away. Both sides are limited before the merge, so the transfer is bounded by
    /// <paramref name="limit"/> either way.
    /// </remarks>
    public async Task<IReadOnlyList<ContinueWatchingEntry>> GetContinueWatchingAsync(
        int sourceId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var movies = await Movies
            .AsNoTracking()
            .Where(movie => movie.SourceId == sourceId && movie.ResumePositionSeconds != null)
            .OrderByDescending(movie => movie.LastWatchedUtc)
            .Take(limit)
            .Select(movie => new ContinueWatchingEntry(
                ContentKind.Movie,
                movie.Id,
                movie.Name,
                string.Empty,
                movie.CoverUrl,
                movie.ResumePositionSeconds!.Value,
                movie.DurationSeconds,
                movie.LastWatchedUtc!.Value))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var episodes = await Episodes
            .AsNoTracking()
            .Where(episode => episode.Season!.Series!.SourceId == sourceId
                && episode.ResumePositionSeconds != null)
            .OrderByDescending(episode => episode.LastWatchedUtc)
            .Take(limit)
            .Select(episode => new
            {
                episode.Id,
                SeriesName = episode.Season!.Series!.Name,
                Cover = episode.Season!.Series!.CoverUrl,
                SeasonNumber = episode.Season!.Number,
                episode.Number,
                episode.Title,
                Position = episode.ResumePositionSeconds!.Value,
                episode.DurationSeconds,
                WatchedAt = episode.LastWatchedUtc!.Value,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // The label is composed here rather than in the query, because it is string formatting SQLite has
        // no expression for and the row set is already bounded by the limit.
        var entries = episodes.Select(episode => new ContinueWatchingEntry(
            ContentKind.Series,
            episode.Id,
            episode.SeriesName,
            EpisodeNaming.Describe(episode.SeasonNumber, episode.Number, episode.Title),
            episode.Cover,
            episode.Position,
            episode.DurationSeconds,
            episode.WatchedAt));

        return
        [
            .. movies
                .Concat(entries)
                .OrderByDescending(entry => entry.LastWatchedUtc)
                .Take(limit),
        ];
    }

    /// <summary>
    /// Records where the viewer left a film.
    /// </summary>
    /// <remarks>
    /// The <see cref="WatchOutcome"/> is translated into columns here rather than by the caller, so that
    /// only one place decides what "finished" does to a row. Written with an update statement rather than
    /// by loading the film: this runs while playback is stopping, and it must not depend on a catalogue
    /// entity still being in hand.
    /// </remarks>
    public Task RecordMovieProgressAsync(
        int movieId,
        WatchOutcome outcome,
        TimeSpan position,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        var resumeAt = ResumeSecondsFor(outcome, position);
        var finished = outcome == WatchOutcome.Finished;

        return Movies
            .Where(movie => movie.Id == movieId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(movie => movie.ResumePositionSeconds, resumeAt)
                    .SetProperty(movie => movie.LastWatchedUtc, atUtc)

                    // Never unset. Opening a film that was already watched through and closing it again
                    // is not un-watching it.
                    .SetProperty(movie => movie.IsWatched, movie => movie.IsWatched || finished),
                cancellationToken);
    }

    /// <summary>Records where the viewer left an episode.</summary>
    public Task RecordEpisodeProgressAsync(
        int episodeId,
        WatchOutcome outcome,
        TimeSpan position,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        var resumeAt = ResumeSecondsFor(outcome, position);
        var finished = outcome == WatchOutcome.Finished;

        return Episodes
            .Where(episode => episode.Id == episodeId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(episode => episode.ResumePositionSeconds, resumeAt)
                    .SetProperty(episode => episode.LastWatchedUtc, atUtc)
                    .SetProperty(episode => episode.IsWatched, episode => episode.IsWatched || finished),
                cancellationToken);
    }

    /// <summary>
    /// The position to store, which only a part-watched item has.
    /// </summary>
    /// <remarks>
    /// Both the discarded and the finished outcomes clear it, for the same reason from opposite ends: an
    /// item that offers to resume at its first minute or at its closing credits is offering nothing.
    /// </remarks>
    private static int? ResumeSecondsFor(WatchOutcome outcome, TimeSpan position)
    {
        return outcome == WatchOutcome.Resumable ? (int)position.TotalSeconds : null;
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

        var existingByExternalId = existing.ToDictionary(movie => movie.ExternalId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var movie in incoming)
        {
            seen.Add(movie.ExternalId);
            movie.CategoryId = ResolveCategoryId(movie.CategoryExternalId, ContentKind.Movie, categoryIds);

            if (!existingByExternalId.TryGetValue(movie.ExternalId, out var stored))
            {
                movie.SourceId = sourceId;
                Movies.Add(movie);
                continue;
            }

            stored.Name = movie.Name;
            stored.CoverUrl = movie.CoverUrl;
            stored.CategoryExternalId = movie.CategoryExternalId;
            stored.CategoryId = movie.CategoryId;
            stored.AddedUtc = movie.AddedUtc;
            stored.SortOrder = movie.SortOrder;

            // Assigned only where the listing has something to say. These four are also what the detail
            // call fills in, and a listing that omits them would otherwise erase it.
            stored.ContainerExtension = movie.ContainerExtension ?? stored.ContainerExtension;
            stored.Plot = movie.Plot ?? stored.Plot;
            stored.Genre = movie.Genre ?? stored.Genre;
            stored.Rating = movie.Rating ?? stored.Rating;
            stored.Year = movie.Year ?? stored.Year;
        }

        Movies.RemoveRange(existing.Where(movie => !seen.Contains(movie.ExternalId)));
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

        var existingByExternalId = existing.ToDictionary(series => series.ExternalId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var series in incoming)
        {
            seen.Add(series.ExternalId);
            series.CategoryId = ResolveCategoryId(series.CategoryExternalId, ContentKind.Series, categoryIds);

            if (!existingByExternalId.TryGetValue(series.ExternalId, out var stored))
            {
                series.SourceId = sourceId;
                Series.Add(series);
                continue;
            }

            stored.Name = series.Name;
            stored.CoverUrl = series.CoverUrl;
            stored.CategoryExternalId = series.CategoryExternalId;
            stored.CategoryId = series.CategoryId;
            stored.SortOrder = series.SortOrder;

            // Left where the listing is silent, exactly as for a film. The detail call is the only source
            // of these on many panels.
            stored.Plot = series.Plot ?? stored.Plot;
            stored.Genre = series.Genre ?? stored.Genre;
            stored.Cast = series.Cast ?? stored.Cast;
            stored.Director = series.Director ?? stored.Director;
            stored.Rating = series.Rating ?? stored.Rating;
            stored.Year = series.Year ?? stored.Year;

            // The one field a refresh must always adopt: it is what tells the stored seasons apart from
            // stale ones, so keeping an older value would leave a changed series never re-fetched.
            stored.LastModifiedUtc = series.LastModifiedUtc;
        }

        Series.RemoveRange(existing.Where(series => !seen.Contains(series.ExternalId)));
    }

    /// <summary>
    /// Brings a stored series' seasons and episodes in line with a detail response, and returns the
    /// episode count.
    /// </summary>
    private static int ReconcileSeasons(Series stored, SeriesDetail detail)
    {
        // Indexed across the whole series rather than per season, so an episode the provider moves between
        // seasons is recognised as the same episode and keeps its position.
        var storedEpisodes = stored.Seasons.SelectMany(season => season.Episodes).ToList();
        var episodesByExternalId = new Dictionary<string, Episode>(StringComparer.Ordinal);

        foreach (var episode in storedEpisodes)
        {
            // TryAdd rather than Add: a provider that lists the same episode under two seasons would
            // otherwise throw here, and the duplicate is dealt with below by keeping only what was matched.
            episodesByExternalId.TryAdd(episode.ExternalId, episode);
        }

        var storedSeasons = stored.Seasons.ToDictionary(season => season.Number);
        var seenSeasons = new HashSet<int>();
        var kept = new HashSet<Episode>();
        var episodeCount = 0;

        foreach (var incomingSeason in detail.Seasons)
        {
            seenSeasons.Add(incomingSeason.Number);

            if (!storedSeasons.TryGetValue(incomingSeason.Number, out var season))
            {
                season = new Season { Number = incomingSeason.Number, Episodes = [] };
                stored.Seasons.Add(season);
                storedSeasons[incomingSeason.Number] = season;
            }

            season.Name = incomingSeason.Name ?? season.Name;
            season.CoverUrl = incomingSeason.CoverUrl ?? season.CoverUrl;
            season.Plot = incomingSeason.Plot ?? season.Plot;

            foreach (var incomingEpisode in incomingSeason.Episodes)
            {
                episodeCount++;

                if (!episodesByExternalId.TryGetValue(incomingEpisode.ExternalId, out var episode))
                {
                    season.Episodes.Add(incomingEpisode);
                    episodesByExternalId[incomingEpisode.ExternalId] = incomingEpisode;
                    kept.Add(incomingEpisode);
                    continue;
                }

                kept.Add(episode);
                episode.Title = incomingEpisode.Title;
                episode.Number = incomingEpisode.Number;
                episode.ContainerExtension = incomingEpisode.ContainerExtension
                    ?? episode.ContainerExtension;
                episode.Plot = incomingEpisode.Plot ?? episode.Plot;
                episode.StillUrl = incomingEpisode.StillUrl ?? episode.StillUrl;
                episode.DurationSeconds = incomingEpisode.DurationSeconds ?? episode.DurationSeconds;
                episode.AddedUtc = incomingEpisode.AddedUtc ?? episode.AddedUtc;

                // Refiled by the provider: move it rather than duplicating it, and the viewer's position
                // travels with the row.
                if (episode.SeasonId != season.Id || !season.Episodes.Contains(episode))
                {
                    RemoveFromCurrentSeason(stored, episode);
                    season.Episodes.Add(episode);
                }
            }
        }

        // Driven by which instances were matched rather than by which identifiers were seen, so that a row
        // the provider has stopped listing goes — and so does the second copy of one it listed twice.
        foreach (var episode in storedEpisodes.Where(item => !kept.Contains(item)))
        {
            RemoveFromCurrentSeason(stored, episode);
        }

        foreach (var season in stored.Seasons.Where(item => !seenSeasons.Contains(item.Number)).ToList())
        {
            stored.Seasons.Remove(season);
        }

        return episodeCount;
    }

    private static void RemoveFromCurrentSeason(Series stored, Episode episode)
    {
        foreach (var season in stored.Seasons)
        {
            if (season.Episodes.Remove(episode))
            {
                return;
            }
        }
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

        // Both instants go through the UTC converter for the reason stated on it: the continue-watching
        // list orders by the time last watched, and EF's default DateTimeOffset mapping cannot be ordered
        // by in SQLite at all.
        movie.Property(entity => entity.AddedUtc).HasConversion(NullableUtcInstantConverter);
        movie.Property(entity => entity.LastWatchedUtc).HasConversion(NullableUtcInstantConverter);

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
