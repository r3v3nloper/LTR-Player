using LTR.Core.Sources;

namespace LTR.Core.Content;

/// <summary>
/// A live channel offered by a source.
/// </summary>
public sealed class Channel
{
    public int Id { get; set; }

    public int SourceId { get; set; }
    public PlaylistSource? Source { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>
    /// The provider's own category identifier, kept alongside the resolved foreign key.
    /// </summary>
    /// <remarks>
    /// Providers cannot know the local <see cref="CategoryId"/>, so they populate this and the
    /// persistence layer resolves the relationship. Retaining it also lets a refresh re-link a
    /// channel whose category was renamed provider-side.
    /// </remarks>
    public string? CategoryExternalId { get; set; }

    /// <summary>
    /// The provider's stream identifier, which becomes part of the playback URL. A string rather
    /// than an integer because M3U playlists identify streams by opaque token, not by number.
    /// </summary>
    public required string ExternalId { get; set; }

    public required string Name { get; set; }

    public string? LogoUrl { get; set; }

    /// <summary>
    /// Guide identifier (<c>epg_channel_id</c> in Xtream, <c>tvg-id</c> in M3U) used to join
    /// against XMLTV programme data. Frequently absent or wrong, hence nullable and matched with a
    /// name-based fallback.
    /// </summary>
    public string? EpgChannelId { get; set; }

    /// <summary>Channel number as presented by the provider, when it supplies one.</summary>
    public int? Number { get; set; }

    /// <summary>
    /// Whether the provider retains a catch-up archive. Read and stored from the start even though
    /// catch-up playback is out of scope, so enabling it later needs no schema migration.
    /// </summary>
    public bool HasArchive { get; set; }

    public int? ArchiveDurationDays { get; set; }

    public bool IsFavorite { get; set; }

    public int SortOrder { get; set; }
}
