namespace LTR.Core.Sources;

/// <summary>
/// A subscription supplied as a plain M3U playlist, with an optional separate XMLTV guide.
/// </summary>
public sealed class M3uSource : PlaylistSource
{
    /// <summary>
    /// Location of the playlist. May be a remote URL or a local file, and typically already
    /// carries any credentials the provider needs inside its query string.
    /// </summary>
    public required Uri PlaylistUrl { get; set; }

    /// <summary>
    /// Separate XMLTV guide location. M3U playlists carry channel identifiers but no programme
    /// data, so without this a plain playlist has no guide at all.
    /// </summary>
    public Uri? EpgUrl { get; set; }

    public override Uri Endpoint => PlaylistUrl;

    /// <remarks>
    /// A playlist has no account. Whatever credentials it carries are inside its query string and the
    /// provider never reports on them, so a failed stream cannot be explained by asking.
    /// </remarks>
    public override bool ReportsAccountState => false;
}
