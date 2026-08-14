using LTR.Core.Sources;

namespace LTR.Core.Content;

/// <summary>
/// A film offered by a source.
/// </summary>
/// <remarks>
/// Deliberately not modelled as a <see cref="Channel"/> with a different <see cref="ContentKind"/>.
/// A film has no guide, no favourite marker and no channel number, but it does have a container
/// extension that is part of its address, a cover the catalogue is browsed by, and a position the
/// viewer left off at. Sharing a table would have meant a row where most columns are meaningless.
/// </remarks>
public sealed class VodItem
{
    public int Id { get; set; }

    public int SourceId { get; set; }
    public PlaylistSource? Source { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>
    /// The provider's own category identifier, kept alongside the resolved foreign key for the same
    /// reason as on <see cref="Channel.CategoryExternalId"/>: providers cannot know local keys.
    /// </summary>
    public string? CategoryExternalId { get; set; }

    /// <summary>
    /// Stable identity within the source — the panel's VOD stream id, which is also what its playback
    /// address is built from.
    /// </summary>
    public required string ExternalId { get; set; }

    public required string Name { get; set; }

    /// <summary>Poster address, which is what the catalogue is browsed by.</summary>
    public string? CoverUrl { get; set; }

    /// <summary>
    /// Container the panel serves this film in, such as <c>mp4</c> or <c>mkv</c>.
    /// </summary>
    /// <remarks>
    /// Part of the address rather than a preference: a film is stored in one container and requesting
    /// another yields a 404. Nullable because a handful of panels omit it from the listing, in which
    /// case the detail call supplies it and playback falls back to <c>mp4</c> until it has.
    /// </remarks>
    public string? ContainerExtension { get; set; }

    public double? Rating { get; set; }

    public int? Year { get; set; }

    public string? Plot { get; set; }

    public string? Genre { get; set; }

    public string? Cast { get; set; }

    public string? Director { get; set; }

    /// <summary>Running time as the provider states it, in seconds.</summary>
    public int? DurationSeconds { get; set; }

    public DateTimeOffset? AddedUtc { get; set; }

    /// <summary>
    /// Whether <c>get_vod_info</c> has been read for this film.
    /// </summary>
    /// <remarks>
    /// A real subscription lists tens of thousands of films, so the detail call cannot be part of an
    /// import — it is made when the film is opened and the answer stored. This flag is what stops it
    /// being made again on every subsequent viewing.
    /// </remarks>
    public bool HasDetail { get; set; }

    /// <summary>Where the viewer left off, in seconds. Null when it was never started.</summary>
    /// <remarks>
    /// User data, like <see cref="Channel.IsFavorite"/>: a catalogue refresh overwrites everything the
    /// provider owns and must leave this alone.
    /// </remarks>
    public int? ResumePositionSeconds { get; set; }

    public DateTimeOffset? LastWatchedUtc { get; set; }

    /// <summary>Watched to the end, which is what takes it off the continue-watching list.</summary>
    public bool IsWatched { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Running time as a duration, or null when the provider states none.</summary>
    public TimeSpan? Duration => DurationSeconds.HasValue ? TimeSpan.FromSeconds(DurationSeconds.Value) : null;
}
