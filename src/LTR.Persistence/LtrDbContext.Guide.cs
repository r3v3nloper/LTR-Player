using LTR.Core.Content;
using Microsoft.EntityFrameworkCore;

namespace LTR.Persistence;

/// <summary>
/// The programme guide half of the unit of work: writing an imported guide, joining it to the channel
/// list, and answering the two questions the interface asks of it.
/// </summary>
/// <remarks>
/// <para>
/// Separated into its own file because the guide is written in a fundamentally different way from the
/// catalogue. A catalogue arrives whole and is reconciled in one pass; a guide arrives as a stream of
/// hundreds of thousands of rows and is written in batches while it is still being read. Keeping the two
/// apart makes each readable, without either leaving the class that owns the database (§3.3.2).
/// </para>
/// <para>
/// Nothing here deletes a whole table. A reimport replaces one guide channel's programmes at a time, so
/// at no point is the user looking at a player with no guide in it — which is what a truncate followed by
/// a slow reinsert would produce.
/// </para>
/// </remarks>
public sealed partial class LtrDbContext
{
    /// <summary>
    /// Stores the guide's channel declarations and returns their local identities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An upsert with no deletions, on purpose. Programmes frequently reference channels the guide never
    /// declared, so this is also called mid-import to register those, and a pass that removed whatever it
    /// was not told about would delete them again on the next batch. Obsolete guide channels are cleared
    /// afterwards by <see cref="RemoveGuideChannelsWithoutProgrammesAsync"/>, once the pruning has
    /// established which ones are genuinely empty.
    /// </para>
    /// <para>
    /// Display names are overwritten because the guide owns them, but never with nothing: a channel first
    /// seen through a programme reference has no name, and letting that erase the name a later
    /// declaration supplied would break name matching for it.
    /// </para>
    /// </remarks>
    public async Task<Dictionary<string, int>> EnsureGuideChannelsAsync(
        int sourceId,
        IReadOnlyList<GuideChannel> declared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declared);

        if (declared.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var externalIds = declared.Select(channel => channel.ExternalId).ToList();

        var existing = await GuideChannels
            .Where(channel => channel.SourceId == sourceId && externalIds.Contains(channel.ExternalId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingByExternalId = existing.ToDictionary(
            channel => channel.ExternalId,
            StringComparer.Ordinal);

        foreach (var incoming in declared)
        {
            if (existingByExternalId.TryGetValue(incoming.ExternalId, out var stored))
            {
                stored.DisplayName = incoming.DisplayName ?? stored.DisplayName;
                stored.IconUrl = incoming.IconUrl ?? stored.IconUrl;
                continue;
            }

            incoming.SourceId = sourceId;
            GuideChannels.Add(incoming);
            existingByExternalId[incoming.ExternalId] = incoming;
        }

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return existingByExternalId.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Id,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Writes one batch of programmes, first discarding what the named guide channels held before.
    /// </summary>
    /// <param name="entries">The batch. Their <c>GuideChannelId</c> must already be resolved.</param>
    /// <param name="replacedGuideChannelIds">
    /// Guide channels appearing for the first time in this import. Their existing programmes are deleted
    /// before the batch is written, which is what makes a reimport a replacement rather than a
    /// duplication — and why the caller has to track which channels it has already cleared.
    /// </param>
    /// <remarks>
    /// The delete and the insert share one transaction, so an import interrupted between them cannot
    /// leave a channel with no programmes at all.
    /// </remarks>
    public async Task AppendGuideProgrammesAsync(
        IReadOnlyList<EpgEntry> entries,
        IReadOnlyCollection<int> replacedGuideChannelIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(replacedGuideChannelIds);

        if (entries.Count == 0 && replacedGuideChannelIds.Count == 0)
        {
            return;
        }

        await using var transaction = await Database.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        if (replacedGuideChannelIds.Count > 0)
        {
            await EpgEntries
                .Where(entry => replacedGuideChannelIds.Contains(entry.GuideChannelId))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (entries.Count > 0)
        {
            EpgEntries.AddRange(entries);
            await SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Untracked afterwards: the change tracker would otherwise accumulate every row of a guide
            // that runs to hundreds of thousands of them, in a context the import holds open throughout.
            foreach (var entry in entries)
            {
                Entry(entry).State = EntityState.Detached;
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Works out which guide channel each of a source's channels takes its programmes from, and records
    /// it. Returns how many channels came out matched.
    /// </summary>
    /// <remarks>
    /// The rules live in <see cref="GuideChannelMatcher"/> and the decision is made in memory: matching
    /// is string normalisation that SQLite cannot express, and it needs to compare every channel against
    /// every guide channel, which is one query each way rather than one query per channel.
    /// </remarks>
    public async Task<int> LinkChannelsToGuideAsync(int sourceId, CancellationToken cancellationToken)
    {
        var channels = await Channels
            .Where(channel => channel.SourceId == sourceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var guideChannels = await GuideChannels
            .AsNoTracking()
            .Where(channel => channel.SourceId == sourceId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var links = GuideChannelMatcher.Match(channels, guideChannels);

        foreach (var channel in channels)
        {
            // Cleared where no match was found, so a channel that lost its guide entry stops showing the
            // programmes of whatever it used to be matched to.
            channel.GuideChannelId = links.TryGetValue(channel.Id, out var guideChannelId)
                ? guideChannelId
                : null;
        }

        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return links.Count;
    }

    /// <summary>
    /// Deletes a source's programmes that ended before <paramref name="cutoffUtc"/>.
    /// </summary>
    /// <remarks>
    /// Guides commonly carry several days of history that no view here shows. Without this the table
    /// grows on every import and never shrinks. Scoped to the source being imported: the retention rule is
    /// the same for every guide, but reaching into another source's data from one source's import is a
    /// side effect nobody reading the call site would expect.
    /// </remarks>
    public Task<int> PruneGuideProgrammesAsync(
        int sourceId,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        return EpgEntries
            .Where(entry => entry.GuideChannel!.SourceId == sourceId && entry.StopUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Removes guide channels of a source that hold no programmes, which is what a guide channel that
    /// has fallen out of the guide looks like once its programmes have been replaced or pruned.
    /// </summary>
    public Task<int> RemoveGuideChannelsWithoutProgrammesAsync(
        int sourceId,
        CancellationToken cancellationToken)
    {
        return GuideChannels
            .Where(channel => channel.SourceId == sourceId && channel.Entries.Count == 0)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Records that a source's guide has just been imported, which is what decides when the next import
    /// is due.
    /// </summary>
    public Task MarkGuideImportedAsync(
        int sourceId,
        DateTimeOffset importedAtUtc,
        CancellationToken cancellationToken)
    {
        return Sources
            .Where(source => source.Id == sourceId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(source => source.LastGuideImportedUtc, importedAtUtc),
                cancellationToken);
    }

    /// <summary>
    /// Reports which guide channel each of a source's channels is matched to.
    /// </summary>
    /// <remarks>
    /// Asked for separately rather than read from a <see cref="Channel"/> the caller already holds. The
    /// link is written by the guide import, which runs long after the channel list was loaded, so an
    /// in-memory channel is stale from the moment an import finishes — and a timeline reading it would
    /// report that nothing has a guide immediately after one was successfully imported.
    /// </remarks>
    public async Task<IReadOnlyDictionary<int, int>> GetGuideLinksAsync(
        int sourceId,
        CancellationToken cancellationToken)
    {
        return await Channels
            .AsNoTracking()
            .Where(channel => channel.SourceId == sourceId && channel.GuideChannelId != null)
            .ToDictionaryAsync(
                channel => channel.Id,
                channel => channel.GuideChannelId!.Value,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reports what is on now and next for every channel of a source that has a guide.
    /// </summary>
    /// <remarks>
    /// One query for the whole list rather than one per row. The two programmes per channel are selected
    /// by the database, so a guide holding a fortnight of listings transfers the two rows each channel
    /// needs and not the fortnight.
    /// </remarks>
    public async Task<IReadOnlyList<ChannelGuideSlice>> GetNowAndNextAsync(
        int sourceId,
        DateTimeOffset atUtc,
        CancellationToken cancellationToken)
    {
        var slices = await Channels
            .AsNoTracking()
            .Where(channel => channel.SourceId == sourceId && channel.GuideChannelId != null)
            .Select(channel => new
            {
                ChannelId = channel.Id,
                Upcoming = EpgEntries
                    .Where(entry => entry.GuideChannelId == channel.GuideChannelId
                        && entry.StopUtc > atUtc)
                    .OrderBy(entry => entry.StartUtc)
                    .Take(2)
                    .ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var results = new List<ChannelGuideSlice>(slices.Count);

        foreach (var slice in slices)
        {
            if (slice.Upcoming.Count == 0)
            {
                continue;
            }

            // The first entry only counts as "now" when it has actually started. A channel whose guide
            // has a gap over the current moment must read as nothing on, not as its next programme
            // already running.
            var first = slice.Upcoming[0];
            var isRunning = first.StartUtc <= atUtc;

            results.Add(new ChannelGuideSlice(
                slice.ChannelId,
                isRunning ? first : null,
                isRunning ? slice.Upcoming.ElementAtOrDefault(1) : first));
        }

        return results;
    }

    /// <summary>
    /// Loads the programmes of specific guide channels that overlap a time window, which is what a
    /// timeline shows.
    /// </summary>
    /// <remarks>
    /// Overlap rather than containment: the programme running when the window opens started before it and
    /// is the one the user most wants to see.
    /// </remarks>
    public async Task<IReadOnlyList<EpgEntry>> GetGuideProgrammesAsync(
        IReadOnlyCollection<int> guideChannelIds,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(guideChannelIds);

        if (guideChannelIds.Count == 0)
        {
            return [];
        }

        return await EpgEntries
            .AsNoTracking()
            .Where(entry => guideChannelIds.Contains(entry.GuideChannelId)
                && entry.StartUtc < toUtc
                && entry.StopUtc > fromUtc)
            .OrderBy(entry => entry.GuideChannelId)
            .ThenBy(entry => entry.StartUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Summarises a source's guide, including how many of its channels the guide actually reaches.
    /// </summary>
    public async Task<GuideSummary> GetGuideSummaryAsync(int sourceId, CancellationToken cancellationToken)
    {
        var guideChannelCount = await GuideChannels
            .CountAsync(channel => channel.SourceId == sourceId, cancellationToken)
            .ConfigureAwait(false);

        var programmeCount = await EpgEntries
            .CountAsync(entry => entry.GuideChannel!.SourceId == sourceId, cancellationToken)
            .ConfigureAwait(false);

        var matchedChannelCount = await Channels
            .CountAsync(
                channel => channel.SourceId == sourceId && channel.GuideChannelId != null,
                cancellationToken)
            .ConfigureAwait(false);

        var totalChannelCount = await Channels
            .CountAsync(channel => channel.SourceId == sourceId, cancellationToken)
            .ConfigureAwait(false);

        var coverageUntilUtc = await EpgEntries
            .Where(entry => entry.GuideChannel!.SourceId == sourceId)
            .MaxAsync(entry => (DateTimeOffset?)entry.StopUtc, cancellationToken)
            .ConfigureAwait(false);

        return new GuideSummary(
            guideChannelCount,
            programmeCount,
            matchedChannelCount,
            totalChannelCount,
            coverageUntilUtc);
    }
}
