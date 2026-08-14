namespace LTR.Core.Content;

/// <summary>
/// A series' seasons and episodes, together with the synopsis fields the detail call also carries.
/// </summary>
/// <remarks>
/// The seasons are unsaved <see cref="Season"/> entities carrying unsaved <see cref="Episode"/> ones,
/// in the same spirit as the categories and channels a provider returns: identifiers stay zero until the
/// persistence layer reconciles them against what is stored.
/// </remarks>
public sealed record SeriesDetail(
    IReadOnlyList<Season> Seasons,
    string? Plot = null,
    string? Genre = null,
    string? Cast = null,
    string? Director = null,
    int? Year = null,
    double? Rating = null)
{
    /// <summary>Every episode of every season, which is what the persistence layer reconciles.</summary>
    public IEnumerable<Episode> Episodes => Seasons.SelectMany(season => season.Episodes);
}
