using LTR.Core.Sources;

namespace LTR.Player.Wpf;

/// <summary>
/// The shell operations source management triggers but does not own.
/// </summary>
/// <remarks>
/// Awaitable methods rather than events, because both have to have finished before the command that
/// triggered them carries on: a refresh must have the new catalogue on screen before it lowers its busy
/// flag and re-enables its own buttons, and playback must have released the stream before the source
/// that stream belongs to is deleted. An event handler cannot be awaited.
/// </remarks>
public interface ISourceCoordinator
{
    /// <summary>
    /// Shows <paramref name="source"/>'s stored catalogue, or empties the list when it is <c>null</c>.
    /// </summary>
    Task ShowCatalogueAsync(PlaylistSource? source, CancellationToken cancellationToken);

    /// <summary>
    /// Gives up the stream in flight, which belongs to the source that is about to change or disappear.
    /// </summary>
    Task ReleasePlaybackAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reports that a source's catalogue has just been imported, so its guide can be brought up to date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not awaited by the caller and deliberately so — this is the one shell operation that must not hold
    /// up the command that triggered it. A guide is a download of tens to hundreds of megabytes, and the
    /// Connect button cannot stay disabled for the length of it.
    /// </para>
    /// <para>
    /// Separate from <see cref="ShowCatalogueAsync"/> rather than folded into it, because that one also
    /// runs when the user merely switches between configured sources, and picking a source from a list is
    /// not an invitation to download a guide.
    /// </para>
    /// </remarks>
    void CatalogueImported(PlaylistSource source);
}
