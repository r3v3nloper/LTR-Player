using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// The <c>server_info</c> object of an authentication response.
/// </summary>
internal sealed class XtreamServerInfoDto
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("port")]
    public string? Port { get; set; }

    [JsonPropertyName("https_port")]
    public string? HttpsPort { get; set; }

    [JsonPropertyName("server_protocol")]
    public string? ServerProtocol { get; set; }

    [JsonPropertyName("timezone")]
    public string? TimeZone { get; set; }
}
