using LTR.Core.Content;
using LTR.Core.Sources;
using LTR.Persistence;
using LTR.Providers;
using Microsoft.Extensions.Logging;

namespace LTR.Catalogue;

/// <summary>
/// Serves a film or series from the store, fetching its detail from the provider when the stored copy
/// will not do.
/// </summary>
internal sealed class VodDetailService : IVodDetailService
{
    private readonly CatalogueUnitOfWork _database;
    private readonly IProviderRegistry _providers;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VodDetailService> _logger;

    public VodDetailService(
        CatalogueUnitOfWork database,
        IProviderRegistry providers,
        TimeProvider timeProvider,
        ILogger<VodDetailService> logger)
    {
        _database = database;
        _providers = providers;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Series?> GetSeriesAsync(
        PlaylistSource source,
        int seriesId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var stored = await _database
            .RunAsync(context => context.GetSeriesDetailAsync(seriesId, cancellationToken))
            .ConfigureAwait(false);

        if (stored is null || stored.HasCurrentDetail)
        {
            return stored;
        }

        var detail = await TryFetchAsync(
                source,
                provider => provider.FetchSeriesDetailAsync(stored.ExternalId, cancellationToken),
                stored.Name)
            .ConfigureAwait(false);

        if (detail is null)
        {
            // The panel could not be reached, or it has nothing to say. Either way the stored copy is what
            // there is, and last week's episode list beats an error.
            return stored;
        }

        var episodeCount = await _database
            .RunAsync(context => context.SaveSeriesDetailAsync(
                seriesId,
                detail,

                // Recorded as the value the listing reported, so that a listing which later moves it is
                // what triggers the next fetch. A source with no last_modified at all is stamped with the
                // fetch time instead, which makes every series it holds fetch once and then stay put.
                stored.LastModifiedUtc ?? _timeProvider.GetUtcNow(),
                _timeProvider.GetUtcNow(),
                cancellationToken))
            .ConfigureAwait(false);

        CatalogueLog.SeriesDetailFetched(_logger, stored.Name, detail.Seasons.Count, episodeCount);

        // Reread rather than patched in memory: the reconciliation assigns identities and may have moved
        // episodes between seasons, and the caller needs what is actually stored.
        return await _database
            .RunAsync(context => context.GetSeriesDetailAsync(seriesId, cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<VodItem?> GetMovieAsync(
        PlaylistSource source,
        int movieId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var stored = await _database
            .RunAsync(context => context.GetMovieAsync(movieId, cancellationToken))
            .ConfigureAwait(false);

        if (stored is null || stored.HasDetail)
        {
            return stored;
        }

        var detail = await TryFetchAsync(
                source,
                provider => provider.FetchMovieDetailAsync(stored.ExternalId, cancellationToken),
                stored.Name)
            .ConfigureAwait(false);

        if (detail is null)
        {
            return stored;
        }

        await _database
            .RunAsync(context => context.SaveMovieDetailAsync(movieId, detail, cancellationToken))
            .ConfigureAwait(false);

        return await _database
            .RunAsync(context => context.GetMovieAsync(movieId, cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the provider for a detail, treating a failure as "no detail".
    /// </summary>
    /// <remarks>
    /// The failure is logged and swallowed on purpose. This runs because the user opened a film or a
    /// series, and a panel that is briefly unreachable should leave them looking at what is stored rather
    /// than at a dialog. The exception is not carried outwards, because nothing above here could act on it
    /// differently.
    /// </remarks>
    private async Task<TDetail?> TryFetchAsync<TDetail>(
        PlaylistSource source,
        Func<IContentProvider, Task<TDetail?>> fetch,
        string itemName)
        where TDetail : class
    {
        try
        {
            var provider = _providers.CreateProvider(source);
            return await fetch(provider).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The user navigated away, or the window is closing.
            throw;
        }
        catch (Exception exception)
        {
            CatalogueLog.DetailFetchFailed(_logger, exception, itemName, source.Name);
            return null;
        }
    }
}
