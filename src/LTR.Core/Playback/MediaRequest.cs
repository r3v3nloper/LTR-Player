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
/// <see cref="StartAt"/> travels with the request rather than being applied by the caller afterwards, so
/// that resuming is one operation and cannot be half-done: a caller that had to seek separately would
/// have to know when the engine has opened enough to accept one. How the engine honours it is its own
/// business — the LibVLC one seeks immediately after the stream reports itself playing, for reasons
/// recorded there.
/// </remarks>
public sealed record MediaRequest(
    Uri Url,
    string UserAgent,
    StreamFormat Format,
    string DisplayName,
    TimeSpan? StartAt = null);
