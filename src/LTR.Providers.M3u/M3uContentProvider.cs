using LTR.Core.Content;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Providers.M3u;

/// <summary>
/// Turns an M3U playlist into domain entities.
/// </summary>
internal sealed class M3uContentProvider : IContentProvider
{
    private readonly M3uSource _source;
    private readonly M3uPlaylistLoader _loader;
    private readonly M3uUrlSanitizer _urlSanitizer;
    private readonly ILogger<M3uContentProvider> _logger;

    /// <summary>
    /// The playlist as fetched, held for the lifetime of this instance.
    /// </summary>
    /// <remarks>
    /// Categories and channels both come from the same document, and that document is a single
    /// multi-megabyte download. Fetching it once per provider instance is what stops a refresh pulling
    /// it twice.
    /// </remarks>
    private M3uPlaylist? _playlist;

    public M3uContentProvider(
        M3uSource source,
        M3uPlaylistLoader loader,
        M3uUrlSanitizer urlSanitizer,
        ILogger<M3uContentProvider> logger)
    {
        _source = source;
        _loader = loader;
        _urlSanitizer = urlSanitizer;
        _logger = logger;
    }

    public PlaylistSource Source => _source;

    /// <summary>
    /// Reports whether the playlist can be retrieved.
    /// </summary>
    /// <remarks>
    /// A plain playlist has no account behind it, so there is nothing to authenticate and no
    /// connection limit to report. Whether the document can be fetched is the only equivalent
    /// question, and an unreported limit is honest rather than a guess at one.
    /// </remarks>
    public async Task<ProviderAccount> AuthenticateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await GetPlaylistAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            M3uLog.PlaylistUnreachable(
                _logger,
                exception,
                _source.Name,
                _urlSanitizer.Sanitize(_source.PlaylistUrl, _source));
            return ProviderAccount.Unauthenticated;
        }

        return new ProviderAccount(
            AccountStatus.Active,
            ExpiresAtUtc: null,
            IsTrial: false,
            MaxConnections: 0,
            ActiveConnections: 0,
            AllowedFormats: [StreamFormat.MpegTs, StreamFormat.HlsPlaylist]);
    }

    public async Task<IReadOnlyList<Category>> FetchCategoriesAsync(
        ContentKind kind,
        CancellationToken cancellationToken)
    {
        // A playlist declares live entries only; it has no notion of films or series.
        if (kind != ContentKind.Live)
        {
            return [];
        }

        var playlist = await GetPlaylistAsync(cancellationToken).ConfigureAwait(false);
        var categories = new List<Category>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in playlist.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.GroupTitle) || !seen.Add(entry.GroupTitle))
            {
                continue;
            }

            categories.Add(new Category
            {
                SourceId = _source.Id,
                // A playlist issues no category ids, so the group name is the identity. It is stable
                // for as long as the provider keeps spelling it the same way, which is all that is
                // available here.
                ExternalId = entry.GroupTitle,
                Name = entry.GroupTitle,
                Kind = ContentKind.Live,
                SortOrder = categories.Count,
            });
        }

        return categories;
    }

    public async Task<IReadOnlyList<Channel>> FetchLiveChannelsAsync(CancellationToken cancellationToken)
    {
        var playlist = await GetPlaylistAsync(cancellationToken).ConfigureAwait(false);

        var channels = new List<Channel>(playlist.Entries.Count);
        var usedIdentities = new HashSet<string>(StringComparer.Ordinal);
        var skippedSeparators = 0;

        foreach (var entry in playlist.Entries)
        {
            if (ChannelNaming.IsSeparatorLabel(entry.DisplayName))
            {
                skippedSeparators++;
                continue;
            }

            channels.Add(new Channel
            {
                SourceId = _source.Id,
                ExternalId = BuildIdentity(entry, usedIdentities),
                Name = entry.DisplayName,
                StreamUrl = entry.Url.AbsoluteUri,
                LogoUrl = entry.LogoUrl,
                EpgChannelId = entry.TvgId,
                CategoryExternalId = entry.GroupTitle,
                Number = entry.ChannelNumber,
                SortOrder = channels.Count,
            });
        }

        if (skippedSeparators > 0)
        {
            M3uLog.SkippedSeparatorRows(_logger, skippedSeparators, _source.Name);
        }

        if (playlist.SkippedEntryCount > 0)
        {
            M3uLog.SkippedMalformedEntries(_logger, playlist.SkippedEntryCount, _source.Name);
        }

        return channels;
    }

    /// <summary>
    /// Always empty: a playlist has no notion of films.
    /// </summary>
    /// <remarks>
    /// Entries do sometimes point at film files, but a playlist states nothing that would let them be
    /// told apart from live channels reliably — no stream type, no cover, no running time. Guessing from
    /// group names would file half a subscription's channels under films, so nothing is guessed.
    /// </remarks>
    public Task<IReadOnlyList<VodItem>> FetchMoviesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<VodItem>>([]);
    }

    /// <summary>Always empty: a playlist has no notion of series, seasons or episodes.</summary>
    public Task<IReadOnlyList<Series>> FetchSeriesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<Series>>([]);
    }

    public Task<MovieDetail?> FetchMovieDetailAsync(string externalId, CancellationToken cancellationToken)
    {
        return Task.FromResult<MovieDetail?>(null);
    }

    public Task<SeriesDetail?> FetchSeriesDetailAsync(string externalId, CancellationToken cancellationToken)
    {
        return Task.FromResult<SeriesDetail?>(null);
    }

    /// <summary>
    /// Produces an identity that is stable across refreshes and unique within the playlist.
    /// </summary>
    /// <remarks>
    /// The guide id is preferred, then a key derived from the name. Playlists do repeat both, so a
    /// counter is appended on collision — without it the unique index on (source, identity) would
    /// reject the import, and picking the URL instead would tie identity to credentials that change
    /// when the subscription is renewed.
    /// </remarks>
    private static string BuildIdentity(M3uEntry entry, HashSet<string> usedIdentities)
    {
        var basis = !string.IsNullOrWhiteSpace(entry.TvgId)
            ? entry.TvgId
            : ChannelNaming.ToIdentityKey(entry.DisplayName);

        if (string.IsNullOrEmpty(basis))
        {
            basis = ChannelNaming.ToIdentityKey(entry.Url.AbsoluteUri);
        }

        if (usedIdentities.Add(basis))
        {
            return basis;
        }

        var suffix = 2;

        while (!usedIdentities.Add($"{basis}#{suffix}"))
        {
            suffix++;
        }

        return $"{basis}#{suffix}";
    }

    private async Task<M3uPlaylist> GetPlaylistAsync(CancellationToken cancellationToken)
    {
        _playlist ??= await _loader.LoadAsync(_source, cancellationToken).ConfigureAwait(false);
        return _playlist;
    }
}
