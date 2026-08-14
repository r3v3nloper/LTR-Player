using LTR.Core.Content;

namespace LTR.Core.Playback;

/// <summary>
/// Everything the playback engine needs to open one stream, with no knowledge of which provider
/// protocol produced it.
/// </summary>
/// <param name="Url">Absolute address of the stream.</param>
/// <param name="UserAgent">
/// Agent the request must carry. Panels commonly reject or throttle unfamiliar agents, so this
/// travels with the URL rather than being a global engine setting.
/// </param>
/// <param name="Format">Container the URL was built to deliver.</param>
/// <param name="DisplayName">Human-readable label for logs and the on-screen display.</param>
/// <param name="StartAt">
/// Where playback should begin, for resuming a part-watched film or episode.
/// </param>
/// <remarks>
/// <see cref="StartAt"/> travels with the request rather than being seeked to after playback starts,
/// and that is not a convenience: a seek issued before the engine has opened the media is discarded,
/// one issued after it has opened plays a second of the beginning first and then jumps, and deciding
/// when "after" has arrived means watching for a state change the engine reports at a different moment
/// for every container. Stating the position up front leaves the engine to honour it while opening.
/// </remarks>
public sealed record MediaRequest(
    Uri Url,
    string UserAgent,
    StreamFormat Format,
    string DisplayName,
    TimeSpan? StartAt = null);
