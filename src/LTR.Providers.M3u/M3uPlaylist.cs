namespace LTR.Providers.M3u;

/// <summary>
/// The result of reading an M3U-Plus playlist.
/// </summary>
/// <param name="Entries">Every usable entry, in the order the playlist declared them.</param>
/// <param name="EpgUrl">
/// Guide location from the <c>x-tvg-url</c> attribute on the header line. A plain playlist carries no
/// programme data of its own, so without this there is no guide at all for the source.
/// </param>
/// <param name="SkippedEntryCount">
/// Entries that were declared but could not be used — a missing URL, an unparseable address, or an
/// <c>#EXTINF</c> line with no display name. Reported rather than silently dropped, because a large
/// count means the playlist is not what the player took it for.
/// </param>
public sealed record M3uPlaylist(
    IReadOnlyList<M3uEntry> Entries,
    Uri? EpgUrl,
    int SkippedEntryCount);
