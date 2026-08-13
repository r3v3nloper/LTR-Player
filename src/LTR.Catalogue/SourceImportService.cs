using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Persistence;
using LTR.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace LTR.Catalogue;

/// <summary>
/// Fetches a source's catalogue through its provider and reconciles it into the local store.
/// </summary>
internal sealed class SourceImportService : ISourceImportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IProviderRegistry _providers;
    private readonly TimeProvider _timeProvider;

    public SourceImportService(
        IServiceScopeFactory scopeFactory,
        IProviderRegistry providers,
        TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _providers = providers;
        _timeProvider = timeProvider;
    }

    public Task<SourceImportResult> ImportAsync(
        PlaylistSource source,
        IProgress<SourceImportStage>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        return RunAsync(source, isNewSource: true, progress, cancellationToken);
    }

    public Task<SourceImportResult> RefreshAsync(
        PlaylistSource source,
        IProgress<SourceImportStage>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        return RunAsync(source, isNewSource: false, progress, cancellationToken);
    }

    /// <summary>
    /// The one statement of the import sequence.
    /// </summary>
    /// <remarks>
    /// Import and refresh differ only in whether the source has to be stored first, so they share this
    /// rather than existing as two nearly identical methods. The order matters: the account is checked
    /// before anything is fetched, so an expired subscription is reported as expired instead of as an
    /// empty catalogue, and capabilities are probed before the fetch because they decide what can be
    /// fetched at all.
    /// </remarks>
    private async Task<SourceImportResult> RunAsync(
        PlaylistSource source,
        bool isNewSource,
        IProgress<SourceImportStage>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(SourceImportStage.Authenticating);

        var provider = _providers.CreateProvider(source);
        var account = await provider.AuthenticateAsync(cancellationToken).ConfigureAwait(false);

        if (!account.IsUsable)
        {
            return SourceImportResult.Rejected(account);
        }

        progress?.Report(SourceImportStage.Probing);

        source.Capabilities = await _providers.GetCapabilityProbe(source)
            .ProbeAsync(source, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(SourceImportStage.FetchingCatalogue);

        var categories = await provider.FetchCategoriesAsync(ContentKind.Live, cancellationToken)
            .ConfigureAwait(false);
        var channels = await provider.FetchLiveChannelsAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report(SourceImportStage.Storing);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<LtrDbContext>();

        var sourceId = isNewSource
            ? await context.AddSourceAsync(source, cancellationToken).ConfigureAwait(false)
            : source.Id;

        // Written on every import, not only the first. A source row is otherwise never rewritten, so a
        // refresh used to probe the panel and throw the answer away — an installation kept whatever
        // capabilities it was created with, and an M3U source re-read its playlist on every guide import to
        // rediscover a guide address the probe had already found.
        await context.UpdateProbeResultAsync(source, cancellationToken).ConfigureAwait(false);

        await context.ReconcileLiveCatalogueAsync(
                sourceId,
                categories,
                channels,
                _timeProvider.GetUtcNow(),
                cancellationToken)
            .ConfigureAwait(false);

        return new SourceImportResult(account, sourceId, channels.Count, categories.Count);
    }
}
