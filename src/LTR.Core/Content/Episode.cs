namespace LTR.Core.Content;

/// <summary>
/// One episode of a season, and the smallest thing in a series that can be played.
/// </summary>
public sealed class Episode
{
    public int Id { get; set; }

    public int SeasonId { get; set; }
    public Season? Season { get; set; }

    /// <summary>
    /// The panel's episode id. This, not the series id, is what an episode's address is built from.
    /// </summary>
    public required string ExternalId { get; set; }

    public required string Title { get; set; }

    /// <summary>Episode number within its season, as the provider counts it.</summary>
    public int Number { get; set; }

    /// <summary>
    /// Container the panel serves this episode in. Episodes of one series do vary — a season added
    /// later is frequently in a different container — so it is stored per episode, not per series.
    /// </summary>
    public string? ContainerExtension { get; set; }

    public string? Plot { get; set; }

    /// <summary>Frame or thumbnail for the episode, where the provider offers one.</summary>
    public string? StillUrl { get; set; }

    public int? DurationSeconds { get; set; }

    public DateTimeOffset? AddedUtc { get; set; }

    /// <summary>Where the viewer left off, in seconds. User data, untouched by a refresh.</summary>
    public int? ResumePositionSeconds { get; set; }

    public DateTimeOffset? LastWatchedUtc { get; set; }

    public bool IsWatched { get; set; }

    public TimeSpan? Duration => DurationSeconds.HasValue ? TimeSpan.FromSeconds(DurationSeconds.Value) : null;
}
