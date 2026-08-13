using LTR.Catalogue;
using LTR.Core.Content;
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

    public Task<IReadOnlyList<Category>> GetLiveCategoriesAsync(int sourceId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Category>>(
            [.. Categories.Where(category => category.SourceId == sourceId)]);
    }

    /// <summary>
    /// Answers now and next the way the real store does, including the rule that decides it.
    /// </summary>
    /// <remarks>
    /// The rule is duplicated here rather than reached for, which is the compromise a fake always makes.
    /// It is safe only because the real query is covered directly by the persistence tests against real
    /// SQLite; what these tests care about is that the answer reaches the rows.
    /// </remarks>
    public Task<IReadOnlyList<ChannelGuideSlice>> GetNowAndNextAsync(
        int sourceId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        var slices = new List<ChannelGuideSlice>();

        foreach (var channel in Channels.Where(item => item.SourceId == sourceId && GuideLinks.ContainsKey(item.Id)))
        {
            var upcoming = Programmes
                .Where(entry => entry.GuideChannelId == GuideLinks[channel.Id] && entry.StopUtc > atUtc)
                .OrderBy(entry => entry.StartUtc)
                .Take(2)
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
}
