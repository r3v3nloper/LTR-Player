namespace LTR.Core.Content;

/// <summary>
/// One part-watched film or episode, as the continue-watching list shows it.
/// </summary>
/// <remarks>
/// A read model rather than the entities themselves. The list mixes two unrelated tables and needs four
/// fields from each; handing back whole films and episodes would mean the caller joining seasons to
/// series just to put a title on the row.
/// </remarks>
/// <param name="Kind">
/// Which table <see cref="ItemId"/> belongs to — <see cref="ContentKind.Movie"/> or
/// <see cref="ContentKind.Series"/> — and therefore which address shape resuming it needs.
/// </param>
/// <param name="ItemId">Local identity of the film or of the episode, never of the series.</param>
/// <param name="Subtitle">Season and episode for an episode; empty for a film.</param>
public sealed record ContinueWatchingEntry(
    ContentKind Kind,
    int ItemId,
    string Title,
    string Subtitle,
    string? CoverUrl,
    int PositionSeconds,
    int? DurationSeconds,
    DateTimeOffset LastWatchedUtc)
{
    public TimeSpan Position => TimeSpan.FromSeconds(PositionSeconds);

    /// <summary>
    /// How far in, as a fraction, or zero when the running time is unknown.
    /// </summary>
    /// <remarks>
    /// Providers omit the running time often enough that a progress bar has to cope with its absence;
    /// zero renders as an empty bar rather than as a wrong one.
    /// </remarks>
    public double Progress =>
        DurationSeconds is > 0 ? Math.Clamp((double)PositionSeconds / DurationSeconds.Value, 0, 1) : 0;
}
