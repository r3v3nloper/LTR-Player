namespace LTR.Core.Content;

/// <summary>
/// One episode together with the season number that labels it.
/// </summary>
/// <remarks>
/// An <see cref="Episode"/> records the season it belongs to only by identifier, and everything that names one
/// needs the season's number. Walking a series is the one place that has both, so it hands both back rather
/// than leaving the caller to load the season again.
/// </remarks>
public sealed record EpisodeInSeries(Episode Episode, int SeasonNumber);
