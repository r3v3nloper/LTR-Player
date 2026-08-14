using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// The <c>info</c> block of a <c>get_vod_info</c> response.
/// </summary>
/// <remarks>
/// Panels disagree about which of these fields they populate and about what they call them: the plot
/// appears as <c>plot</c> or <c>description</c>, the people as <c>cast</c> or <c>actors</c>. Both
/// spellings are read and the mapper takes whichever is present.
/// </remarks>
internal sealed class XtreamMovieInfoDto
{
    [JsonPropertyName("plot")]
    public string? Plot { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("cast")]
    public string? Cast { get; set; }

    [JsonPropertyName("actors")]
    public string? Actors { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("releasedate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    /// <summary>Running time in seconds, where the panel measured the file.</summary>
    [JsonPropertyName("duration_secs")]
    public int? DurationSeconds { get; set; }

    /// <summary>
    /// Running time in whole minutes, which is what panels that never opened the file report instead.
    /// </summary>
    [JsonPropertyName("episode_run_time")]
    public int? RunTimeMinutes { get; set; }
}
