using LTR.Core.Sources;

namespace LTR.Core.Content;

/// <summary>
/// A series offered by a source, with its seasons once they have been fetched.
/// </summary>
/// <remarks>
/// A listing carries only what is shown while browsing. Seasons and episodes arrive from a separate
/// <c>get_series_info</c> call, made when the series is opened rather than during an import: a
/// subscription lists thousands of series, and one call each would take hours and hammer the panel.
/// </remarks>
public sealed class Series
{
    public int Id { get; set; }

    public int SourceId { get; set; }
    public PlaylistSource? Source { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public string? CategoryExternalId { get; set; }

    /// <summary>The panel's series id, which the detail call is made with.</summary>
    public required string ExternalId { get; set; }

    public required string Name { get; set; }

    public string? CoverUrl { get; set; }

    public string? Plot { get; set; }

    public string? Genre { get; set; }

    public string? Cast { get; set; }

    public string? Director { get; set; }

    public double? Rating { get; set; }

    public int? Year { get; set; }

    /// <summary>
    /// When the provider last changed this series, as it reports it.
    /// </summary>
    /// <remarks>
    /// Stored so that cached seasons can be told apart from stale ones: a series gaining an episode
    /// moves this, and that is the signal to fetch the detail again rather than showing a season that
    /// stops one episode short.
    /// </remarks>
    public DateTimeOffset? LastModifiedUtc { get; set; }

    /// <summary>When the seasons below were read, or null while they never have been.</summary>
    public DateTimeOffset? DetailFetchedUtc { get; set; }

    /// <summary>The provider's <see cref="LastModifiedUtc"/> as it stood when the detail was read.</summary>
    /// <remarks>
    /// Compared against the current value rather than against a clock. A series nobody has changed is
    /// never re-fetched however old the cached copy is, and one that changed a minute ago is.
    /// </remarks>
    public DateTimeOffset? DetailModifiedUtc { get; set; }

    public int SortOrder { get; set; }

    public ICollection<Season> Seasons { get; set; } = [];

    /// <summary>Whether the stored seasons still match what the provider says it holds.</summary>
    public bool HasCurrentDetail =>
        DetailFetchedUtc.HasValue && DetailModifiedUtc == LastModifiedUtc;
}
