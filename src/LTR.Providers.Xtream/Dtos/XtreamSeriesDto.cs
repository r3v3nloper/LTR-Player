using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// One entry of a <c>get_series</c> response.
/// </summary>
/// <remarks>
/// A series listing is unusually generous by Xtream standards — most panels return the synopsis and
/// cover here — but it never contains seasons or episodes. Those come from <c>get_series_info</c>.
/// </remarks>
internal sealed class XtreamSeriesDto
{
    [JsonPropertyName("num")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Identifier the detail call is made with. Not part of any playback address.</summary>
    [JsonPropertyName("series_id")]
    public string? SeriesId { get; set; }

    [JsonPropertyName("cover")]
    public string? Cover { get; set; }

    [JsonPropertyName("plot")]
    public string? Plot { get; set; }

    [JsonPropertyName("cast")]
    public string? Cast { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    /// <summary>Spelled with a capital D here, unlike the film listing's <c>releasedate</c>.</summary>
    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }

    /// <summary>
    /// When the provider last changed the series, as Unix seconds.
    /// </summary>
    /// <remarks>
    /// The one field that makes caching seasons safe: it moves when an episode is added, which is the
    /// signal to read the detail again instead of showing a season that stops an episode short.
    /// </remarks>
    [JsonPropertyName("last_modified")]
    public long? LastModifiedUnixSeconds { get; set; }
}
