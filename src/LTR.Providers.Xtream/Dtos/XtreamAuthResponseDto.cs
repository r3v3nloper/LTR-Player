using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// Response to a bare <c>player_api.php</c> call, which doubles as the login endpoint.
/// </summary>
internal sealed class XtreamAuthResponseDto
{
    [JsonPropertyName("user_info")]
    public XtreamUserInfoDto? UserInfo { get; set; }

    [JsonPropertyName("server_info")]
    public XtreamServerInfoDto? ServerInfo { get; set; }
}
