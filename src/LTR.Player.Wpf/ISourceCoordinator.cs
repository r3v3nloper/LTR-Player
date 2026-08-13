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
}
