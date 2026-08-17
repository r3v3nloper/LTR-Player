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

    /// <summary>
    /// When the provider was last asked for this film's detail, whatever it answered.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="HasDetail"/>, and the distinction is the point: a panel that answers with
    /// nothing leaves that flag unset, so without this there is no way to tell "never asked" from "asked,
    /// and there is nothing" — and every viewing asked again. Also distinct from
    /// <see cref="Series.DetailFetchedUtc"/>, which records a successful read; this records the asking.
    /// </remarks>
    public DateTimeOffset? DetailAttemptedUtc { get; set; }

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

    /// <summary>
    /// How long an empty answer is taken at its word before the provider is asked again.
    /// </summary>
    /// <remarks>
    /// A day, because an empty answer today is not proof of an empty answer next week: panels do fill their
    /// detail in over time, and a film whose synopsis arrives eventually should pick it up. Long enough that
    /// browsing a catalogue asks once per film, short enough that nobody has to know this rule exists.
    /// </remarks>
    public static TimeSpan DetailRetryInterval => TimeSpan.FromDays(1);

    /// <summary>
    /// Whether asking the provider for this film's detail again is worth a request.
    /// </summary>
    /// <remarks>
    /// A method rather than a computed property, because the answer depends on the clock and an entity must
    /// not read one. It also keeps it off the schema without the explicit <c>Ignore</c> every computed
    /// property in this model needs.
    /// </remarks>
    public bool NeedsDetailFetch(DateTimeOffset asOf)
    {
        if (HasDetail)
        {
            return false;
        }

        return DetailAttemptedUtc is not { } attempted || asOf - attempted >= DetailRetryInterval;
    }

    /// <summary>
    /// Takes on what a film *listing* owns from a freshly fetched copy of this film.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two kinds of field, and the difference is the point. The first group is the listing's to state and is
    /// assigned outright. The second is stated by <c>get_vod_info</c> on most panels and by the listing on
    /// some, so it is taken only where the listing has something to say — assigning it unconditionally would
    /// erase every synopsis the player had fetched, one refresh at a time.
    /// </para>
    /// <para>
    /// The viewer's own data — the resume position, whether it was watched, when — is not here at all, and
    /// neither is <see cref="HasDetail"/>: a refresh must not make the player forget it has the detail.
    /// </para>
    /// </remarks>
    public void AdoptListingFields(VodItem fetched)
    {
        ArgumentNullException.ThrowIfNull(fetched);

        Name = fetched.Name;
        CoverUrl = fetched.CoverUrl;
        CategoryExternalId = fetched.CategoryExternalId;
        CategoryId = fetched.CategoryId;
        AddedUtc = fetched.AddedUtc;
        SortOrder = fetched.SortOrder;

        ContainerExtension = fetched.ContainerExtension ?? ContainerExtension;
        Plot = fetched.Plot ?? Plot;
        Genre = fetched.Genre ?? Genre;
        Rating = fetched.Rating ?? Rating;
        Year = fetched.Year ?? Year;
    }
}
