using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// The <c>info</c> block of one episode.
/// </summary>
internal sealed class XtreamEpisodeInfoDto
{
    [JsonPropertyName("plot")]
    public string? Plot { get; set; }

    [JsonPropertyName("movie_image")]
    public string? Image { get; set; }

    [JsonPropertyName("duration_secs")]
    public int? DurationSeconds { get; set; }

    /// <summary>Whole minutes, reported instead of seconds by panels that never opened the file.</summary>
    [JsonPropertyName("episode_run_time")]
    public int? RunTimeMinutes { get; set; }
}
