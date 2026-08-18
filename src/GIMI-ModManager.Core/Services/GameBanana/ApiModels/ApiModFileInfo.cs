using System.Text.Json.Serialization;

namespace GIMI_ModManager.Core.Services.GameBanana.ApiModels;

public class ApiModFileInfo
{
    [JsonPropertyName("_idRow")] public int FileId { get; init; } = -1;
    [JsonPropertyName("_sFile")] public string FileName { get; init; } = null!;
    [JsonPropertyName("_sDownloadUrl")] public string DownloadUrl { get; init; } = null!;

    [JsonPropertyName("_tsDateAdded")] public long DateAdded { get; init; } = -1;

    [JsonPropertyName("_sDescription")] public string? Description { get; init; }

    [JsonPropertyName("_nFilesize")] public long FileSize { get; init; } = -1;

    [JsonPropertyName("_sAnalysisState")] public string? AnalysisState { get; init; }

    [JsonPropertyName("_sAnalysisResult")] public string? AnalysisResult { get; init; }

    [JsonPropertyName("_sAnalysisResultVerbose")] public string? AnalysisResultVerbose { get; init; }

    [JsonPropertyName("_sAvState")] public string? AvState { get; init; }

    [JsonPropertyName("_sMd5Checksum")] public string Md5Checksum { get; init; } = null!;

    [JsonPropertyName("_nDownloadCount")] public int DownloadCount { get; init; } = -1;

    [JsonPropertyName("_sVersion")] public string? Version { get; init; }

    [JsonPropertyName("_sAvResult")] public string? AvResult { get; init; }

    [JsonPropertyName("_bIsArchived")] public bool IsArchived { get; init; }

    [JsonPropertyName("_bHasContents")] public bool HasContents { get; init; }
}
