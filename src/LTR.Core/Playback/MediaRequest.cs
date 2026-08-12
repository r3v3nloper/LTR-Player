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
public sealed record MediaRequest(Uri Url, string UserAgent, StreamFormat Format, string DisplayName);
