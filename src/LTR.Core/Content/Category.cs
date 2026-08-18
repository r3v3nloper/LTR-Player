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

    /// <summary>
    /// Whether the viewer has pinned this category to the top of the picker.
    /// </summary>
    /// <remarks>
    /// The viewer's own data, exactly as a favourite channel is, and for a related reason: a panel lists its
    /// categories in whatever order it holds them, and the two or three somebody actually watches are as
    /// likely to be at the bottom of two hundred as anywhere else.
    /// </remarks>
    public bool IsFavorite { get; set; }

    public ICollection<Channel> Channels { get; set; } = [];

    /// <summary>
    /// Takes on everything the provider owns from a freshly fetched copy of this category.
    /// </summary>
    /// <remarks>
    /// Neither the identifier nor the kind is copied: together they are what matched the two in the first
    /// place, and a panel numbers its identifiers per section, so the kind is part of the identity. Nor is
    /// <see cref="IsFavorite"/>, which is the viewer's and not the provider's — a refresh that adopted it
    /// would unpin every category on every import.
    /// </remarks>
    public void AdoptProviderFields(Category fetched)
    {
        ArgumentNullException.ThrowIfNull(fetched);

        Name = fetched.Name;
        SortOrder = fetched.SortOrder;
    }
}
