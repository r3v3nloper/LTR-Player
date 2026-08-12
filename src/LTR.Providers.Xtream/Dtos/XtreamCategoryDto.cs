using System.Text.Json.Serialization;

namespace LTR.Providers.Xtream.Dtos;

/// <summary>
/// One entry of a <c>get_*_categories</c> response.
/// </summary>
internal sealed class XtreamCategoryDto
{
    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    [JsonPropertyName("parent_id")]
    public int ParentId { get; set; }
}
