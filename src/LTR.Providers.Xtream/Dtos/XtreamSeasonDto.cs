using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// One entry of the <c>seasons</c> array of a <c>get_series_info</c> response.
/// </summary>
internal sealed class XtreamSeasonDto
{
    [JsonPropertyName("season_number")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("cover")]
    public string? Cover { get; set; }

    [JsonPropertyName("cover_big")]
    public string? CoverBig { get; set; }
}
