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

        var attempt = await TryFetchAsync(
                source,
                provider => provider.FetchSeriesDetailAsync(stored.ExternalId, cancellationToken),
                stored.Name)
            .ConfigureAwait(false);

        // A series needs no equivalent of the film's "asked, and there is nothing": its re-fetch is decided
        // by the provider's own last_modified rather than by a clock, so an unchanged series is never asked
        // twice however empty the answer was.
        if (attempt.Detail is not { } detail)
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

        var askedAt = _timeProvider.GetUtcNow();

        if (stored is null || !stored.NeedsDetailFetch(askedAt))
        {
            return stored;
        }

        var attempt = await TryFetchAsync(
                source,
                provider => provider.FetchMovieDetailAsync(stored.ExternalId, cancellationToken),
                stored.Name)
            .ConfigureAwait(false);

        if (attempt.Detail is not { } detail)
        {
            // An answer of "nothing" is recorded so the next viewing does not ask again; a provider that
            // could not be reached gave no answer at all, and recording that as one would suppress the
            // retry for a day over a momentary outage.
            if (attempt.ProviderAnswered)
            {
                await _database
                    .RunAsync(context =>
                        context.RecordMovieDetailAbsentAsync(movieId, askedAt, cancellationToken))
                    .ConfigureAwait(false);
            }

            return stored;
        }

        await _database
            .RunAsync(context => context.SaveMovieDetailAsync(movieId, detail, askedAt, cancellationToken))
            .ConfigureAwait(false);

        return await _database
            .RunAsync(context => context.GetMovieAsync(movieId, cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asks the provider for a detail, treating a failure as "no detail" for the caller's purposes.
    /// </summary>
    /// <remarks>
    /// The failure is logged and swallowed on purpose. This runs because the user opened a film or a
    /// series, and a panel that is briefly unreachable should leave them looking at what is stored rather
    /// than at a dialog. The exception is not carried outwards, because nothing above here could act on it
    /// differently — but whether the provider answered at all *is* carried out, because a panel with nothing
    /// to say and a panel that said nothing are the same <see langword="null"/> and must not be remembered
    /// the same way.
    /// </remarks>
    private async Task<DetailAttempt<TDetail>> TryFetchAsync<TDetail>(
        PlaylistSource source,
        Func<IContentProvider, Task<TDetail?>> fetch,
        string itemName)
        where TDetail : class
    {
        try
        {
            var provider = _providers.CreateProvider(source);
            var detail = await fetch(provider).ConfigureAwait(false);

            return new DetailAttempt<TDetail>(detail, ProviderAnswered: true);
        }
        catch (OperationCanceledException)
        {
            // The user navigated away, or the window is closing.
            throw;
        }
        catch (Exception exception)
        {
            CatalogueLog.DetailFetchFailed(_logger, exception, itemName, source.Name);
            return new DetailAttempt<TDetail>(Detail: null, ProviderAnswered: false);
        }
    }

    /// <param name="ProviderAnswered">
    /// Whether the provider was reached and replied, whatever it replied. A reply of "nothing" is an answer;
    /// an unreachable host is not.
    /// </param>
    private readonly record struct DetailAttempt<TDetail>(TDetail? Detail, bool ProviderAnswered)
        where TDetail : class;
}
