namespace LTR.Core.Content;

/// <summary>
/// What a detail call adds to a film already known from a listing.
/// </summary>
/// <remarks>
/// A patch rather than a whole entity, and every field is nullable, because panels populate this block
/// very unevenly — the same call returns a full synopsis on one panel and nothing but a container
/// extension on the next. Applying it field by field means a sparse answer cannot blank out what the
/// listing already supplied.
/// </remarks>
/// <param name="ContainerExtension">
/// The one field that affects playback rather than presentation: some panels omit it from the listing
/// and state it only here.
/// </param>
public sealed record MovieDetail(
    string? Plot = null,
    string? Genre = null,
    string? Cast = null,
    string? Director = null,
    int? Year = null,
    double? Rating = null,
    int? DurationSeconds = null,
    string? ContainerExtension = null);
