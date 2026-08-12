using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// One entry of a <c>get_live_streams</c> response.
/// </summary>
internal sealed class XtreamLiveStreamDto
{
    /// <summary>Channel number as ordered by the provider.</summary>
    [JsonPropertyName("num")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Identifier that becomes part of the playback URL.</summary>
    [JsonPropertyName("stream_id")]
    public string? StreamId { get; set; }

    [JsonPropertyName("stream_icon")]
    public string? StreamIcon { get; set; }

    /// <summary>Guide identifier, joined against XMLTV programme data.</summary>
    [JsonPropertyName("epg_channel_id")]
    public string? EpgChannelId { get; set; }

    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }

    /// <summary>Set when the provider retains a catch-up archive for this channel.</summary>
    [JsonPropertyName("tv_archive")]
    public bool HasArchive { get; set; }

    [JsonPropertyName("tv_archive_duration")]
    public int ArchiveDurationDays { get; set; }
}
