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
    /// Stable identity of the channel within its source, used to match a refresh against what is
    /// already stored.
    /// </summary>
    /// <remarks>
    /// For Xtream this is the numeric stream id. For M3U it is the guide id, or a key derived from the
    /// name when there is none — deliberately not the stream URL, because those embed credentials that
    /// change when a subscription is renewed, and an identity that changes takes the user's favourites
    /// with it.
    /// </remarks>
    public required string ExternalId { get; set; }

    /// <summary>
    /// Complete playback address, for sources that state one outright.
    /// </summary>
    /// <remarks>
    /// M3U playlists carry the full URL per entry, so there is nothing to construct. Xtream sources
    /// leave this empty and have their address built from the stream id, which is why it is nullable
    /// rather than required.
    /// </remarks>
    public string? StreamUrl { get; set; }

    public required string Name { get; set; }

    public string? LogoUrl { get; set; }

    /// <summary>
    /// Guide identifier (<c>epg_channel_id</c> in Xtream, <c>tvg-id</c> in M3U) used to join
    /// against XMLTV programme data. Frequently absent or wrong, hence nullable and matched with a
    /// name-based fallback.
    /// </summary>
    public string? EpgChannelId { get; set; }

    /// <summary>
    /// The guide channel this one was matched to, once a guide has been imported.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="EpgChannelId"/>, which the provider owns and a refresh
    /// overwrites. This is the outcome of matching — by guide id where there is one, by name otherwise —
    /// and it is written only by the guide import, so a catalogue refresh does not silently discard it.
    /// </remarks>
    public int? GuideChannelId { get; set; }

    public GuideChannel? GuideChannel { get; set; }

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
