using System.Text.Json.Serialization;

namespace GIMI_ModManager.Core.Services.GameBanana.ApiModels;

/// <summary>
/// Represents a category (e.g. character name) from Gamebanana's Mod/Categories endpoint.
/// </summary>
public class ApiCategoryItem
{
    [JsonPropertyName("_idRow")] public int CategoryId { get; init; }
    [JsonPropertyName("_sName")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("_nItemCount")] public int ItemCount { get; init; }
    [JsonPropertyName("_nCategoryCount")] public int CategoryCount { get; init; }
    [JsonPropertyName("_sUrl")] public string? Url { get; init; }
    [JsonPropertyName("_sIconUrl")] public string? IconUrl { get; init; }
}