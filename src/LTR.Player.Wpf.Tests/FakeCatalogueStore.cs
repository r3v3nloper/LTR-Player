using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;

namespace LTR.Player.Wpf;

/// <summary>
/// An in-memory catalogue, so the view model can be exercised without a database.
/// </summary>
internal sealed class FakeCatalogueStore : ICatalogueStore
{
    public List<PlaylistSource> Sources { get; } = [];

    public List<Channel> Channels { get; } = [];

    public List<Category> Categories { get; } = [];

    public List<int> DeletedSourceIds { get; } = [];

    /// <summary>Favourite changes that were written, so a test can prove one was persisted.</summary>
    public List<(int ChannelId, bool IsFavorite)> FavoriteWrites { get; } = [];

    public List<GuideChannel> GuideChannels { get; } = [];

    public List<EpgEntry> Programmes { get; } = [];

    /// <summary>
    /// Channel identity to guide channel identity, as the database holds it.
    /// </summary>
    /// <remarks>
    /// Kept here rather than on the <see cref="Channel"/> instances on purpose, because that is the
    /// distinction that matters: a guide import writes this long after the channel list was loaded, so a
    /// channel object in the view layer is stale and anything reading its link is wrong.
    /// </remarks>
    public Dictionary<int, int> GuideLinks { get; } = [];

    public Task<IReadOnlyList<PlaylistSource>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<PlaylistSource>>(Sources);
    }

    /// <summary>
    /// When set, loading channels blocks until cancelled, standing in for the seventeen thousand rows a real
    /// subscription makes the shell wait for.
    /// </summary>
    public bool BlockChannelLoad { get; set; }

    public async Task<IReadOnlyList<Channel>> GetLiveChannelsAsync(
        int sourceId,
        CancellationToken cancellationToken)
    {
        if (BlockChannelLoad)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        return [.. Channels.Where(channel => channel.SourceId == sourceId)];
    }

    public Task<IReadOnlyList<Category>> GetCategoriesAsync(
        int sourceId,
        ContentKind kind,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Category>>(
            [.. Categories.Where(category => category.SourceId == sourceId && category.Kind == kind)]);
    }

    /// <summary>
    /// Answers now and next the way the real store does, including the rule that decides it.
    /// </summary>
    /// <remarks>
    /// The rule is duplicated here rather than reached for, which is the compromise a fake always makes.
    /// It is safe only because the real query is covered directly by the persistence tests against real
    /// SQLite; what these tests care about is that the answer reaches the rows.
    /// </remarks>
    /// <summary>How many times now-and-next was asked for, so a test can prove it was not.</summary>
    public int NowAndNextQueries { get; private set; }

    public Task<IReadOnlyList<ChannelGuideSlice>> GetNowAndNextAsync(
        int sourceId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        NowAndNextQueries++;

        var slices = new List<ChannelGuideSlice>();

        foreach (var channel in Channels.Where(item => item.SourceId == sourceId && GuideLinks.ContainsKey(item.Id)))
        {
            var upcoming = Programmes
                .Where(entry => entry.GuideChannelId == GuideLinks[channel.Id] && entry.StopUtc > atUtc)
                .OrderBy(entry => entry.StartUtc)
                .Take(2)
                .Select(entry => new GuideProgrammeSummary(entry.Title, entry.StartUtc, entry.StopUtc))
                .ToList();

            if (upcoming.Count == 0)
            {
                continue;
            }

            var isRunning = upcoming[0].StartUtc <= atUtc;

            slices.Add(new ChannelGuideSlice(
                channel.Id,
                isRunning ? upcoming[0] : null,
                isRunning ? upcoming.ElementAtOrDefault(1) : upcoming[0]));
        }

        return Task.FromResult<IReadOnlyList<ChannelGuideSlice>>(slices);
    }

    public Task<IReadOnlyDictionary<int, int>> GetGuideLinksAsync(
        int sourceId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyDictionary<int, int>>(
            Channels
                .Where(channel => channel.SourceId == sourceId && GuideLinks.ContainsKey(channel.Id))
                .ToDictionary(channel => channel.Id, channel => GuideLinks[channel.Id]));
    }

    public Task<IReadOnlyList<EpgEntry>> GetGuideProgrammesAsync(
        IReadOnlyCollection<int> guideChannelIds,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<EpgEntry>>(
        [
            .. Programmes
                .Where(entry => guideChannelIds.Contains(entry.GuideChannelId)
                    && entry.StartUtc < toUtc
                    && entry.StopUtc > fromUtc)
                .OrderBy(entry => entry.StartUtc),
        ]);
    }

    public Task<GuideSummary> GetGuideSummaryAsync(int sourceId, CancellationToken cancellationToken)
    {
        var channels = Channels.Where(channel => channel.SourceId == sourceId).ToList();

        return Task.FromResult(new GuideSummary(
            GuideChannels.Count,
            Programmes.Count,
            channels.Count(channel => GuideLinks.ContainsKey(channel.Id)),
            channels.Count,
            Programmes.Count == 0 ? null : Programmes.Max(entry => entry.StopUtc)));
    }

    public Task SetFavoriteAsync(int channelId, bool isFavorite, CancellationToken cancellationToken)
    {
        FavoriteWrites.Add((channelId, isFavorite));
        return Task.CompletedTask;
    }

    public Task DeleteSourceAsync(int sourceId, CancellationToken cancellationToken)
    {
        DeletedSourceIds.Add(sourceId);
        Sources.RemoveAll(source => source.Id == sourceId);
        return Task.CompletedTask;
    }

    public List<VodItem> Movies { get; } = [];

    public List<Series> SeriesCatalogue { get; } = [];

    public List<Episode> Episodes { get; } = [];

    public List<ContinueWatchingEntry> ContinueWatching { get; } = [];

    /// <summary>
    /// Progress that was written, so a test can prove a position was persisted and with what verdict.
    /// </summary>
    public List<(ContentKind Kind, int ItemId, WatchOutcome Outcome, TimeSpan Position)> ProgressWrites
    {
        get;
    } = [];

    public Task<IReadOnlyList<VodItem>> GetMoviesAsync(int sourceId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<VodItem>>(
            [.. Movies.Where(movie => movie.SourceId == sourceId)]);
    }

    public Task<IReadOnlyList<Series>> GetSeriesAsync(int sourceId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Series>>(
            [.. SeriesCatalogue.Where(series => series.SourceId == sourceId)]);
    }

    /// <summary>
    /// Applies the filter the way the real store's SQL does, and caps the page the same way.
    /// </summary>
    /// <remarks>
    /// The rules are reached for rather than duplicated — <see cref="CatalogueFilter"/> is the same type the
    /// query is built from — so what differs here is only that this one runs in memory. The database
    /// translation itself is covered against real SQLite in the persistence tests.
    /// </remarks>
    public Task<CataloguePage<VodItem>> SearchMoviesAsync(
        int sourceId,
        CatalogueFilter filter,
        int limit,
        CancellationToken cancellationToken)
    {
        var matching = Movies
            .Where(movie => movie.SourceId == sourceId
                && filter.Matches(movie.Name, movie.CategoryExternalId))
            .ToList();

        return Task.FromResult(new CataloguePage<VodItem>([.. matching.Take(limit)], matching.Count));
    }

    public Task<CataloguePage<Series>> SearchSeriesAsync(
        int sourceId,
        CatalogueFilter filter,
        int limit,
        CancellationToken cancellationToken)
    {
        var matching = SeriesCatalogue
            .Where(series => series.SourceId == sourceId
                && filter.Matches(series.Name, series.CategoryExternalId))
            .ToList();

        return Task.FromResult(new CataloguePage<Series>([.. matching.Take(limit)], matching.Count));
    }

    public Task<VodItem?> GetMovieAsync(int movieId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Movies.FirstOrDefault(movie => movie.Id == movieId));
    }

    public Task<Episode?> GetEpisodeAsync(int episodeId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Episodes.FirstOrDefault(episode => episode.Id == episodeId));
    }

    public Task<IReadOnlyList<ContinueWatchingEntry>> GetContinueWatchingAsync(
        int sourceId,
        int limit,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ContinueWatchingEntry>>(
        [
            .. ContinueWatching
                .OrderByDescending(entry => entry.LastWatchedUtc)
                .Take(limit),
        ]);
    }

    public Task RecordMovieProgressAsync(
        int movieId,
        WatchOutcome outcome,
        TimeSpan position,
        CancellationToken cancellationToken)
    {
        ProgressWrites.Add((ContentKind.Movie, movieId, outcome, position));
        return Task.CompletedTask;
    }

    public Task RecordEpisodeProgressAsync(
        int episodeId,
        WatchOutcome outcome,
        TimeSpan position,
        CancellationToken cancellationToken)
    {
        ProgressWrites.Add((ContentKind.Series, episodeId, outcome, position));
        return Task.CompletedTask;
    }
}
