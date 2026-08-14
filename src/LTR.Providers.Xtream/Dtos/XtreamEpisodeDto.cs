using System.Text.Json.Serialization;
using LTR.Providers.Xtream.Json;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// One episode within a <c>get_series_info</c> response.
/// </summary>
internal sealed class XtreamEpisodeDto
{
    /// <summary>
    /// The episode's own identifier, which is what its playback address is built from — not the
    /// series id, and not the season.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("episode_num")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Container this episode is stored in. Varies within one series, because a season added later is
    /// frequently encoded differently, so it is never assumed from a sibling.
    /// </summary>
    [JsonPropertyName("container_extension")]
    public string? ContainerExtension { get; set; }

    /// <summary>
    /// Season the episode belongs to, as stated on the episode itself. Trusted over the key it was
    /// filed under, since the two disagree on panels that key by season name.
    /// </summary>
    [JsonPropertyName("season")]
    public int? Season { get; set; }

    [JsonPropertyName("added")]
    public long? AddedUnixSeconds { get; set; }

    [JsonPropertyName("info")]
    [JsonConverter(typeof(TolerantObjectConverter<XtreamEpisodeInfoDto>))]
    public XtreamEpisodeInfoDto? Info { get; set; }
}
