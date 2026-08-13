using LTR.Core.Sources;

namespace LTR.Providers;

/// <summary>
/// Supplies the XMLTV programme guide belonging to a source, where there is one.
/// </summary>
/// <remarks>
/// <para>
/// Finding the guide is protocol knowledge and belongs with the protocol: an Xtream panel serves it from
/// <c>xmltv.php</c> if it serves it at all, while an M3U playlist either names a guide in its header or
/// has none. Neither fact should reach the importer.
/// </para>
/// <para>
/// The guide is handed over as a stream to a callback rather than returned. A guide is tens to hundreds
/// of megabytes, so it has to be consumed while the response is still open — and this way the
/// implementation keeps ownership of that response and closes it, instead of handing out a stream whose
/// lifetime nobody clearly owns.
/// </para>
/// </remarks>
public interface IGuideSource
{
    bool Supports(PlaylistSource source);

    /// <summary>
    /// Opens the source's guide and passes it to <paramref name="read"/>.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the source offers no guide at all — a panel without
    /// <c>xmltv.php</c>, or a playlist that names none. That is a fact about the source rather than a
    /// failure, so it is reported rather than thrown, and <paramref name="read"/> is not called.
    /// </returns>
    Task<bool> TryReadGuideAsync(
        PlaylistSource source,
        Func<Stream, CancellationToken, Task> read,
        CancellationToken cancellationToken);
}
