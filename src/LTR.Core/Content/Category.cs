using LTR.Core.Sources;

namespace LTR.Core.Content;

/// <summary>
/// A provider-defined grouping of channels or items, such as "DE | Sport".
/// </summary>
public sealed class Category
{
    public int Id { get; set; }

    public int SourceId { get; set; }
    public PlaylistSource? Source { get; set; }

    /// <summary>
    /// Identifier as issued by the provider. Only unique within one source, and not stable across
    /// provider-side reorganisations, so it is never used as the primary key.
    /// </summary>
    public required string ExternalId { get; set; }

    public required string Name { get; set; }

    public ContentKind Kind { get; set; }

    /// <summary>Position as delivered by the provider, so its intended ordering is preserved.</summary>
    public int SortOrder { get; set; }

    public ICollection<Channel> Channels { get; set; } = [];
}
