using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// The <c>user_info</c> object of an authentication response.
/// </summary>
internal sealed class XtreamUserInfoDto
{
    [JsonPropertyName("username")]
    public string? Username { get; set; }

    /// <summary>Non-zero when the credentials were accepted.</summary>
    [JsonPropertyName("auth")]
    public int Auth { get; set; }

    /// <summary>Free text such as <c>Active</c>, <c>Expired</c> or <c>Banned</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>Unix seconds at which access lapses; absent for unlimited accounts.</summary>
    [JsonPropertyName("exp_date")]
    public long? ExpiryUnixSeconds { get; set; }

    [JsonPropertyName("is_trial")]
    public bool IsTrial { get; set; }

    /// <summary>Streams the panel currently counts as open for this account.</summary>
    [JsonPropertyName("active_cons")]
    public int ActiveConnections { get; set; }

    /// <summary>Streams the account may hold open at once.</summary>
    [JsonPropertyName("max_connections")]
    public int MaxConnections { get; set; }

    [JsonPropertyName("allowed_output_formats")]
    public List<string>? AllowedOutputFormats { get; set; }
}
