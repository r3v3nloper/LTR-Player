namespace LTR.Core.Content;

/// <summary>
/// What is on one channel right now, and what follows it.
/// </summary>
/// <remarks>
/// A read model rather than a pair of navigation properties: the channel list asks this question for
/// every visible row at once, and answering it by loading each channel's programmes would be thousands
/// of queries for two rows apiece.
/// </remarks>
/// <param name="ChannelId">The channel this describes.</param>
/// <param name="Now">
/// The programme covering the instant asked about, or <see langword="null"/> when the guide has a gap
/// there — which is normal near the end of a guide's coverage.
/// </param>
/// <param name="Next">The programme starting after <paramref name="Now"/>, when the guide reaches it.</param>
public sealed record ChannelGuideSlice(int ChannelId, EpgEntry? Now, EpgEntry? Next);
