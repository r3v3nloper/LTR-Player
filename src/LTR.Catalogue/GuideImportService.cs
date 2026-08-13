using LTR.Core.Sources;
using LTR.Epg.Xmltv;
using LTR.Providers;
using Microsoft.Extensions.Logging;

namespace LTR.Catalogue;

/// <summary>
/// Fetches a source's XMLTV guide, stores it, and joins it to that source's channels.
/// </summary>
/// <remarks>
/// The one statement of the guide import sequence, in the layer that already owns catalogue import.
/// Composition only: locating the guide is the provider's knowledge, reading it is the XMLTV reader's,
/// storing it is the writer's, and matching it to channels belongs to the domain. What is here is the
/// order those happen in and the decision of what to keep.
/// </remarks>
internal sealed class GuideImportService : IGuideImportService
{
    /// <summary>
    /// Programmes per write. Large enough that a guide is a few hundred round trips rather than a
    /// hundred thousand, small enough that a batch is a few hundred kilobytes.
    /// </summary>
    private const int ProgrammeBatchSize = 2_000;

    /// <summary>
    /// How much of the past to keep. Guides commonly carry days of history; a few hours is enough to
    /// answer "what was that programme that just ended" and the rest is dead weight.
    /// </summary>
    private static readonly TimeSpan HistoryRetention = TimeSpan.FromHours(6);

    /// <summary>
    /// How far ahead to keep. Guides occasionally state programmes years out, invariably as filler.
    /// </summary>
    private static readonly TimeSpan FutureHorizon = TimeSpan.FromDays(21);

    private readonly CatalogueUnitOfWork _database;
    private readonly IProviderRegistry _providers;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GuideImportService> _logger;

    public GuideImportService(
        CatalogueUnitOfWork database,
        IProviderRegistry providers,
        TimeProvider timeProvider,
        ILogger<GuideImportService> logger)
    {
        _database = database;
        _providers = providers;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GuideImportResult> ImportAsync(
        PlaylistSource source,
        IProgress<GuideImportStage>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var startedAt = _timeProvider.GetUtcNow();

        progress?.Report(GuideImportStage.Locating);

        var writer = new GuideProgrammeWriter(
            source.Id,
            _database,
            ProgrammeBatchSize,
            startedAt - HistoryRetention,
            startedAt + FutureHorizon);

        var stopTimeFiller = new XmltvStopTimeFiller(writer);
        XmltvReadResult? readResult = null;

        var guideExists = await _providers.GetGuideSource(source)
            .TryReadGuideAsync(
                source,
                async (stream, token) =>
                {
                    progress?.Report(GuideImportStage.Reading);

                    readResult = await XmltvStreamReader.ReadAsync(stream, stopTimeFiller, token)
                        .ConfigureAwait(false);

                    // Both are required and in this order: the filler still holds the last programme of
                    // every channel, and the writer still holds a partial batch.
                    await stopTimeFiller.CompleteAsync(token).ConfigureAwait(false);
                    await writer.FlushAsync(token).ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!guideExists)
        {
            return GuideImportResult.NoGuideAvailable;
        }

        if (readResult is null || writer.StoredProgrammeCount == 0)
        {
            CatalogueLog.GuideContainedNothing(_logger, source.Name);
            return GuideImportResult.Empty;
        }

        progress?.Report(GuideImportStage.Matching);

        var matchedChannels = await _database
            .RunAsync(context => context.LinkChannelsToGuideAsync(source.Id, cancellationToken))
            .ConfigureAwait(false);

        progress?.Report(GuideImportStage.Pruning);

        await PruneAsync(source.Id, startedAt - HistoryRetention, cancellationToken).ConfigureAwait(false);

        var completedAt = _timeProvider.GetUtcNow();

        await _database
            .RunAsync(context => context.MarkGuideImportedAsync(source.Id, completedAt, cancellationToken))
            .ConfigureAwait(false);

        // The caller holds an untracked instance of the source, and it is the instance the staleness check
        // reads. Left alone, the next refresh would import the guide all over again.
        source.LastGuideImportedUtc = completedAt;

        var summary = await _database
            .RunAsync(context => context.GetGuideSummaryAsync(source.Id, cancellationToken))
            .ConfigureAwait(false);

        CatalogueLog.GuideImported(
            _logger,
            source.Name,
            summary.ProgrammeCount,
            summary.MatchedChannelCount,
            summary.TotalChannelCount,
            (completedAt - startedAt).TotalSeconds);

        if (readResult.WasTruncated)
        {
            CatalogueLog.GuideWasTruncated(_logger, source.Name, writer.StoredProgrammeCount);
        }

        return new GuideImportResult(
            GuideImportOutcome.Imported,
            writer.StoredProgrammeCount,
            matchedChannels,
            readResult.WasTruncated,
            summary);
    }

    public Task<GuideImportResult> ImportIfStaleAsync(
        PlaylistSource source,
        IProgress<GuideImportStage>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.LastGuideImportedUtc is { } importedAt
            && _timeProvider.GetUtcNow() - importedAt < IGuideImportService.StaleAfter)
        {
            CatalogueLog.GuideStillFresh(_logger, source.Name, importedAt);
            return Task.FromResult(GuideImportResult.NotDue);
        }

        return ImportAsync(source, progress, cancellationToken);
    }

    /// <summary>
    /// Discards what has already been broadcast, then the guide channels left holding nothing.
    /// </summary>
    /// <remarks>
    /// In that order deliberately: a guide channel that dropped out of the guide keeps its old programmes
    /// until they age out, and only then does it become recognisable as obsolete. Removing it earlier
    /// would mean guessing.
    /// </remarks>
    private async Task PruneAsync(int sourceId, DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        var prunedProgrammes = await _database
            .RunAsync(context => context.PruneGuideProgrammesAsync(sourceId, cutoffUtc, cancellationToken))
            .ConfigureAwait(false);

        var prunedChannels = await _database
            .RunAsync(context =>
                context.RemoveGuideChannelsWithoutProgrammesAsync(sourceId, cancellationToken))
            .ConfigureAwait(false);

        if (prunedProgrammes > 0 || prunedChannels > 0)
        {
            CatalogueLog.GuidePruned(_logger, prunedProgrammes, prunedChannels);
        }
    }
}
