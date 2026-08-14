using System.Text.Json.Serialization;
using LTR.Providers.Xtream.Json;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// A <c>get_vod_info</c> response, which splits what it knows across two blocks.
/// </summary>
/// <remarks>
/// <c>info</c> carries the presentation fields and <c>movie_data</c> the playback ones — including the
/// container extension, which is the one field here that decides whether the film can be opened at all.
/// </remarks>
internal sealed class XtreamVodInfoResponseDto
{
    /// <remarks>
    /// A panel with nothing to say answers <c>[]</c> here rather than <c>null</c>, which is why both
    /// blocks are read tolerantly.
    /// </remarks>
    [JsonPropertyName("info")]
    [JsonConverter(typeof(TolerantObjectConverter<XtreamMovieInfoDto>))]
    public XtreamMovieInfoDto? Info { get; set; }

    [JsonPropertyName("movie_data")]
    [JsonConverter(typeof(TolerantObjectConverter<XtreamMovieDataDto>))]
    public XtreamMovieDataDto? MovieData { get; set; }
}
