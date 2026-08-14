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

    /// <summary>
    /// Builds the address for a film.
    /// </summary>
    /// <param name="startAt">
    /// Where playback should begin, for resuming a part-watched film. Carried into the request rather
    /// than seeked to afterwards, because a seek issued before the engine has opened the file is
    /// ignored — and one issued after it has is a visible jump.
    /// </param>
    /// <exception cref="NotSupportedException">
    /// The source's protocol is not handled, or it offers no films.
    /// </exception>
    MediaRequest ResolveMovie(PlaylistSource source, VodItem movie, TimeSpan? startAt = null);

    /// <summary>
    /// Builds the address for one episode of a series.
    /// </summary>
    /// <remarks>
    /// Takes the episode alone. An episode's address is built from its own identifier, not from its
    /// series' or its season's, so nothing else is needed.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// The source's protocol is not handled, or it offers no series.
    /// </exception>
    MediaRequest ResolveEpisode(PlaylistSource source, Episode episode, TimeSpan? startAt = null);
}
