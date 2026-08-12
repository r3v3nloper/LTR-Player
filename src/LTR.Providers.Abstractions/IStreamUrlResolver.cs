using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;

namespace LTR.Providers;

/// <summary>
/// Builds playable stream addresses from stored catalogue data.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="IContentProvider"/> because URL construction performs no I/O.
/// That keeps the rules that vary most between panels — path segments, container extensions,
/// credential escaping — under plain unit test rather than behind an HTTP boundary.
/// </remarks>
public interface IStreamUrlResolver
{
    bool Supports(PlaylistSource source);

    /// <summary>
    /// Builds the address for a live channel.
    /// </summary>
    /// <exception cref="NotSupportedException">The source's protocol is not handled.</exception>
    MediaRequest ResolveLive(PlaylistSource source, Channel channel);
}
