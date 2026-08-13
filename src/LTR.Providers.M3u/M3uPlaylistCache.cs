namespace LTR.Providers.M3u;

/// <summary>
/// Holds the most recently parsed playlist for a short while.
/// </summary>
/// <remarks>
/// <para>
/// Importing a source loads the same document twice: once to check it can be retrieved at all, and
/// once by the capability probe, which is resolved separately and so has its own loader. For a
/// subscription playlist of several megabytes that is a wasted download every time a source is added
/// or refreshed.
/// </para>
/// <para>
/// One slot and a short lifetime on purpose. The duplication happens within a single import, seconds
/// apart, so that is all the cache needs to cover — and a longer-lived cache would start serving stale
/// catalogues to a user who pressed refresh precisely because something changed.
/// </para>
/// </remarks>
internal sealed class M3uPlaylistCache
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _timeProvider;
    private readonly Lock _slotLock = new();

    private Uri? _cachedUrl;
    private M3uPlaylist? _cachedPlaylist;
    private DateTimeOffset _cachedAtUtc;

    public M3uPlaylistCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public bool TryGet(Uri playlistUrl, out M3uPlaylist playlist)
    {
        lock (_slotLock)
        {
            if (_cachedPlaylist is not null
                && _cachedUrl == playlistUrl
                && _timeProvider.GetUtcNow() - _cachedAtUtc < Lifetime)
            {
                playlist = _cachedPlaylist;
                return true;
            }
        }

        playlist = null!;
        return false;
    }

    public void Store(Uri playlistUrl, M3uPlaylist playlist)
    {
        lock (_slotLock)
        {
            _cachedUrl = playlistUrl;
            _cachedPlaylist = playlist;
            _cachedAtUtc = _timeProvider.GetUtcNow();
        }
    }
}
