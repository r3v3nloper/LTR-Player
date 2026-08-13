namespace LTR.Core.Content;

/// <summary>
/// How much guide data a source holds, and how much of it is reachable from its channels.
/// </summary>
/// <remarks>
/// The matched count is the number that matters. A guide can import perfectly and still be useless if
/// its channel names do not resolve against the subscription's, and no other figure would reveal that.
/// </remarks>
/// <param name="GuideChannelCount">Channels the guide describes.</param>
/// <param name="ProgrammeCount">Programmes stored.</param>
/// <param name="MatchedChannelCount">Channels of the source that resolved to a guide channel.</param>
/// <param name="TotalChannelCount">Channels of the source altogether.</param>
/// <param name="CoverageUntilUtc">
/// End of the last programme stored, which is how far ahead the guide reaches.
/// </param>
public sealed record GuideSummary(
    int GuideChannelCount,
    int ProgrammeCount,
    int MatchedChannelCount,
    int TotalChannelCount,
    DateTimeOffset? CoverageUntilUtc);
