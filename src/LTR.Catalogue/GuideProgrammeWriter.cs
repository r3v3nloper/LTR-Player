using LTR.Core.Content;
using LTR.Epg.Xmltv;

namespace LTR.Catalogue;

/// <summary>
/// Writes an XMLTV document into the database as it is read, in batches.
/// </summary>
/// <remarks>
/// <para>
/// This is where a guide import stays affordable. The reader pushes hundreds of thousands of programmes
/// past, and holding them would cost more memory than the compressed document; a batch of a couple of
/// thousand is written and forgotten, so the cost is flat regardless of guide size.
/// </para>
/// <para>
/// It also performs the per-channel replacement. The first time a batch contains a programme for a
/// given guide channel, that channel's stored programmes are deleted along with the write — so a
/// reimport replaces channel by channel and the user is never left looking at a player whose guide has
/// been emptied.
/// </para>
/// </remarks>
internal sealed class GuideProgrammeWriter : IXmltvSink
{
    private readonly int _sourceId;
    private readonly CatalogueUnitOfWork _database;
    private readonly int _batchSize;
    private readonly DateTimeOffset _earliestKept;
    private readonly DateTimeOffset _latestKept;

    /// <summary>Guide channel declarations awaiting their first write.</summary>
    private readonly List<GuideChannel> _pendingChannels = [];

    /// <summary>
    /// Identifiers already declared or already stored. Guides do declare the same channel twice, and the
    /// unique index on (source, identifier) turns a duplicate into a failed import.
    /// </summary>
    private readonly HashSet<string> _knownExternalIds = new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> _guideChannelIds = new(StringComparer.Ordinal);

    /// <summary>
    /// Guide channels whose stored programmes have already been discarded in this import, so a channel
    /// whose programmes span two batches is cleared once rather than twice.
    /// </summary>
    private readonly HashSet<int> _replacedGuideChannelIds = [];

    private List<XmltvProgramme> _batch;

    public GuideProgrammeWriter(
        int sourceId,
        CatalogueUnitOfWork database,
        int batchSize,
        DateTimeOffset earliestKept,
        DateTimeOffset latestKept)
    {
        _sourceId = sourceId;
        _database = database;
        _batchSize = batchSize;
        _earliestKept = earliestKept;
        _latestKept = latestKept;
        _batch = new List<XmltvProgramme>(batchSize);
    }

    public int StoredProgrammeCount { get; private set; }

    /// <summary>
    /// Programmes discarded for falling outside the window worth keeping, or for ending before they
    /// started.
    /// </summary>
    public int DiscardedProgrammeCount { get; private set; }

    public ValueTask ChannelAsync(XmltvChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (_knownExternalIds.Add(channel.Id))
        {
            _pendingChannels.Add(new GuideChannel
            {
                SourceId = _sourceId,
                ExternalId = channel.Id,
                DisplayName = channel.DisplayName,
                IconUrl = channel.IconUrl,
            });
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask ProgrammeAsync(XmltvProgramme programme, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programme);

        if (!IsWorthKeeping(programme))
        {
            DiscardedProgrammeCount++;
            return;
        }

        _batch.Add(programme);

        if (_batch.Count >= _batchSize)
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes whatever is buffered. Must be called once reading has finished, or the last partial batch
    /// and any channel declarations that never saw a programme are lost.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var batch = _batch;
        _batch = new List<XmltvProgramme>(_batchSize);

        await RegisterPendingChannelsAsync(batch, cancellationToken).ConfigureAwait(false);

        var entries = new List<EpgEntry>(batch.Count);
        var toReplace = new List<int>();

        foreach (var programme in batch)
        {
            if (!_guideChannelIds.TryGetValue(programme.ChannelId, out var guideChannelId))
            {
                // Unreachable in practice: every referenced identifier was registered just above. Counted
                // rather than thrown, because losing a guide over one unexplained reference would be a
                // poor trade.
                DiscardedProgrammeCount++;
                continue;
            }

            if (_replacedGuideChannelIds.Add(guideChannelId))
            {
                toReplace.Add(guideChannelId);
            }

            entries.Add(ToEntry(programme, guideChannelId));
        }

        if (entries.Count == 0 && toReplace.Count == 0)
        {
            return;
        }

        await _database
            .RunAsync(context => context.AppendGuideProgrammesAsync(entries, toReplace, cancellationToken))
            .ConfigureAwait(false);

        StoredProgrammeCount += entries.Count;
    }

    /// <summary>
    /// Persists the channel declarations read so far, together with any identifier this batch references
    /// without one.
    /// </summary>
    /// <remarks>
    /// Guides do reference channels they never declared. Registering those keeps their programmes rather
    /// than dropping them — they can still be matched by guide id, which is how the channels carrying one
    /// find their listings.
    /// </remarks>
    private async Task RegisterPendingChannelsAsync(
        List<XmltvProgramme> batch,
        CancellationToken cancellationToken)
    {
        foreach (var programme in batch)
        {
            if (_knownExternalIds.Add(programme.ChannelId))
            {
                _pendingChannels.Add(new GuideChannel
                {
                    SourceId = _sourceId,
                    ExternalId = programme.ChannelId,
                });
            }
        }

        if (_pendingChannels.Count == 0)
        {
            return;
        }

        var registered = await _database
            .RunAsync(context => context.EnsureGuideChannelsAsync(_sourceId, _pendingChannels, cancellationToken))
            .ConfigureAwait(false);

        foreach (var pair in registered)
        {
            _guideChannelIds[pair.Key] = pair.Value;
        }

        _pendingChannels.Clear();
    }

    private bool IsWorthKeeping(XmltvProgramme programme)
    {
        if (programme.StopUtc is not { } stopUtc || stopUtc <= programme.StartUtc)
        {
            return false;
        }

        return stopUtc > _earliestKept && programme.StartUtc < _latestKept;
    }

    private static EpgEntry ToEntry(XmltvProgramme programme, int guideChannelId)
    {
        return new EpgEntry
        {
            GuideChannelId = guideChannelId,
            StartUtc = programme.StartUtc,

            // Non-null by the time a programme reaches here: the stop-time filler has closed every entry
            // the document left open, and IsWorthKeeping rejects anything still without an end.
            StopUtc = programme.StopUtc!.Value,
            Title = programme.Title,
            Description = programme.Description,
            Category = programme.Category,
            EpisodeReference = programme.EpisodeReference,
            IconUrl = programme.IconUrl,
        };
    }
}
