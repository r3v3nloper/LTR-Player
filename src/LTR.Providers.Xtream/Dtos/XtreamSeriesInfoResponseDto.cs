using System.Text.Json;
using System.Text.Json.Serialization;
using LTR.Providers.Xtream.Json;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// A <c>get_series_info</c> response: the series' own fields, its declared seasons and its episodes.
/// </summary>
internal sealed class XtreamSeriesInfoResponseDto
{
    /// <summary>The same shape as a film's <c>info</c> block, and populated just as unevenly.</summary>
    [JsonPropertyName("info")]
    [JsonConverter(typeof(TolerantObjectConverter<XtreamMovieInfoDto>))]
    public XtreamMovieInfoDto? Info { get; set; }

    /// <summary>
    /// Seasons as the panel declares them, which is frequently an empty array even for a series with
    /// eight of them. Used only to name and illustrate seasons the episodes already established.
    /// </summary>
    [JsonPropertyName("seasons")]
    public List<XtreamSeasonDto>? Seasons { get; set; }

    /// <summary>
    /// The episodes, kept as raw JSON because panels disagree about the shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The documented form is an object keyed by season number — <c>{"1": [...], "2": [...]}</c> — but
    /// several forks emit a bare array of season arrays instead, and one emits an object whose keys are
    /// season names. A typed property would deserialise exactly one of those and throw on the rest,
    /// losing the whole series over its container. The mapper inspects the shape instead.
    /// </para>
    /// <para>
    /// Safe to keep past the response document's lifetime: the converter for
    /// <see cref="JsonElement"/> parses into a document of its own rather than pointing into the one
    /// being read.
    /// </para>
    /// </remarks>
    [JsonPropertyName("episodes")]
    public JsonElement Episodes { get; set; }
}
