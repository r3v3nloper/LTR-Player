using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// The <c>movie_data</c> block of a <c>get_vod_info</c> response: what is needed to play the film.
/// </summary>
internal sealed class XtreamMovieDataDto
{
    [JsonPropertyName("stream_id")]
    public string? StreamId { get; set; }

    [JsonPropertyName("container_extension")]
    public string? ContainerExtension { get; set; }
}
