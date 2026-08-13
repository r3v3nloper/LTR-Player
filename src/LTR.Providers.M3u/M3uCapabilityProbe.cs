using LTR.Core.Sources;

namespace LTR.Providers.M3u;

/// <summary>
/// States what an M3U source can do.
/// </summary>
/// <remarks>
/// Nothing is discovered over the network here, because there is nothing to discover: a playlist is a
/// flat list of live entries with no endpoints to test. The one variable is whether the header named a
/// guide, and that is settled by parsing rather than probing.
/// </remarks>
internal sealed class M3uCapabilityProbe : IProviderCapabilityProbe
{
    private readonly M3uPlaylistLoader _loader;
    private readonly TimeProvider _timeProvider;

    public M3uCapabilityProbe(M3uPlaylistLoader loader, TimeProvider timeProvider)
    {
        _loader = loader;
        _timeProvider = timeProvider;
    }

    public bool Supports(PlaylistSource source)
    {
        return source is M3uSource;
    }

    public async Task<ProviderCapabilities> ProbeAsync(PlaylistSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source is not M3uSource m3uSource)
        {
            throw new NotSupportedException(
                $"{nameof(M3uCapabilityProbe)} handles M3U sources only, but got {source.GetType().Name}.");
        }

        var playlist = await _loader.LoadAsync(m3uSource, cancellationToken).ConfigureAwait(false);

        // A guide named in the header is adopted, so the user does not have to supply it a second time.
        m3uSource.EpgUrl ??= playlist.EpgUrl;

        return new ProviderCapabilities
        {
            SupportsLive = playlist.Entries.Count > 0,
            SupportsVod = false,
            SupportsSeries = false,
            SupportsXmltvEpg = m3uSource.EpgUrl is not null,

            // No per-channel guide endpoint exists for a playlist; programme data can only come from a
            // full XMLTV document.
            SupportsShortEpg = false,

            // Entries state their own addresses, so both containers may appear and neither is chosen
            // by this player.
            SupportsMpegTs = true,
            SupportsHls = true,

            // Meaningless for a playlist: no URL is constructed, so there is no path shape to decide.
            RequiresLivePathSegment = false,

            ProbedAtUtc = _timeProvider.GetUtcNow(),
        };
    }
}
