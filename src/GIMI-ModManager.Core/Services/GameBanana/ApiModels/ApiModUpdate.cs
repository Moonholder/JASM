using System.Text.Json;
using System.Text.Json.Serialization;

namespace GIMI_ModManager.Core.Services.GameBanana.ApiModels;

/// <summary>
/// Represents an update entry from the /Mod/{id}/Updates API endpoint.
/// </summary>
public sealed class ApiModUpdate
{
    [JsonPropertyName("_idRow")] public int UpdateId { get; init; }
    [JsonPropertyName("_sName")] public string? Title { get; init; }
    [JsonPropertyName("_sText")] public string? Text { get; init; }
    [JsonPropertyName("_sVersion")] public string? Version { get; init; }
    [JsonPropertyName("_tsDateAdded")] public long DateAdded { get; init; }
    [JsonPropertyName("_bIsSignificant")] public bool IsSignificant { get; init; }

    [JsonPropertyName("_aChangeLog")] public JsonElement ChangeLog { get; init; }
    [JsonPropertyName("_bHasFiles")] public bool HasFiles { get; init; }
    [JsonPropertyName("_aFiles")] public JsonElement Files { get; init; }
}

/// <summary>
/// Wrapper for the paginated /Mod/{id}/Updates API response.
/// </summary>
public sealed class ApiModUpdateList
{
    [JsonPropertyName("_aRecords")] public List<ApiModUpdate>? Records { get; init; }
}