using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// One entry of a <c>get_vod_streams</c> response.
/// </summary>
/// <remarks>
/// Newer panels put a synopsis and a release date in the listing as well; older ones state only what
/// is needed to draw a poster and build an address. Everything beyond the identifier and the name is
/// therefore optional here, and the detail call fills in what is missing.
/// </remarks>
internal sealed class XtreamVodStreamDto
{
    [JsonPropertyName("num")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Identifier that becomes part of the playback URL.</summary>
    [JsonPropertyName("stream_id")]
    public string? StreamId { get; set; }

    [JsonPropertyName("stream_icon")]
    public string? StreamIcon { get; set; }

    /// <summary>
    /// Container the film is stored in, such as <c>mp4</c>. Part of the address, not a preference.
    /// </summary>
    [JsonPropertyName("container_extension")]
    public string? ContainerExtension { get; set; }

    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    /// <summary>When the provider added the film, as Unix seconds.</summary>
    [JsonPropertyName("added")]
    public long? AddedUnixSeconds { get; set; }

    [JsonPropertyName("plot")]
    public string? Plot { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    /// <summary>
    /// Release date, spelled all lowercase here and <c>releaseDate</c> in the series listing. Only the
    /// year is kept; the rest is not shown anywhere.
    /// </summary>
    [JsonPropertyName("releasedate")]
    public string? ReleaseDate { get; set; }
}
