namespace LTR.Core.Content;

/// <summary>
/// One season of a series.
/// </summary>
/// <remarks>
/// Derived from the episodes rather than trusted from the panel. Many panels return no season list at
/// all and key their episodes by season number instead, so the numbers present in the episode map are
/// what defines the seasons; a declared season list, where one exists, only supplies the name and cover.
/// </remarks>
public sealed class Season
{
    public int Id { get; set; }

    public int SeriesId { get; set; }
    public Series? Series { get; set; }

    /// <summary>
    /// Season number as the provider counts it. Zero is legitimate and means specials or extras.
    /// </summary>
    public int Number { get; set; }

    public string? Name { get; set; }

    public string? CoverUrl { get; set; }

    public string? Plot { get; set; }

    public ICollection<Episode> Episodes { get; set; } = [];
}
