using System.Text.Json.Serialization;

namespace GIMI_ModManager.Core.Services.GameBanana.ApiModels;

/// <summary>
/// Wrapper for paginated Gamebanana API responses (Subfeed, Mod/Index, Search).
/// </summary>
public record ApiPaginatedResponse<T>
{
    [JsonPropertyName("_aMetadata")]
    public ApiPaginationMetadata? Metadata { get; init; }

    [JsonPropertyName("_aRecords")]
    public List<T> Records { get; init; } = new();
}

public record ApiPaginationMetadata
{
    [JsonPropertyName("_nRecordCount")]
    public int RecordCount { get; init; }

    [JsonPropertyName("_nPerpage")]
    public int PerPage { get; init; }

    [JsonPropertyName("_bIsComplete")]
    public bool IsComplete { get; init; }
}

/// <summary>
/// A single mod record from the Subfeed / Mod Index / Search endpoints.
/// All three endpoints share this identical structure.
/// </summary>
public record ApiModRecord
{
    [JsonPropertyName("_idRow")]
    public int ModId { get; init; }

    [JsonPropertyName("_sModelName")]
    public string ModelName { get; init; } = "Mod";

    [JsonPropertyName("_sName")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("_sProfileUrl")]
    public string? ProfileUrl { get; init; }

    [JsonPropertyName("_tsDateAdded")]
    public long DateAdded { get; init; }

    [JsonPropertyName("_tsDateModified")]
    public long DateModified { get; init; }

    [JsonPropertyName("_bHasFiles")]
    public bool HasFiles { get; init; }

    [JsonPropertyName("_aSubmitter")]
    public ApiModSubmitter? Submitter { get; init; }

    [JsonPropertyName("_aPreviewMedia")]
    public ApiPreviewMedia? PreviewMedia { get; init; }

    [JsonPropertyName("_nLikeCount")]
    public int LikeCount { get; init; }

    [JsonPropertyName("_nViewCount")]
    public int ViewCount { get; init; }

    [JsonPropertyName("_nPostCount")]
    public int PostCount { get; init; }

    [JsonPropertyName("_sInitialVisibility")]
    public string? InitialVisibility { get; init; }

    [JsonPropertyName("_aRootCategory")]
    public ApiRootCategory? RootCategory { get; init; }

    /// <summary>
    /// Computed thumbnail URL from the first preview image.
    /// </summary>
    public string? ThumbnailUrl
    {
        get
        {
            var img = PreviewMedia?.Images?.FirstOrDefault();
            if (img == null) return null;
            var file = img.File220 ?? img.File;
            return string.IsNullOrEmpty(file) ? null : $"{img.BaseUrl}/{file}";
        }
    }
}

public record ApiModSubmitter
{
    [JsonPropertyName("_idRow")]
    public int Id { get; init; }

    [JsonPropertyName("_sName")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("_sProfileUrl")]
    public string? ProfileUrl { get; init; }

    [JsonPropertyName("_sAvatarUrl")]
    public string? AvatarUrl { get; init; }
}

public record ApiPreviewMedia
{
    [JsonPropertyName("_aImages")]
    public List<ApiPreviewImage>? Images { get; init; }
}

public record ApiPreviewImage
{
    [JsonPropertyName("_sBaseUrl")]
    public string BaseUrl { get; init; } = string.Empty;

    [JsonPropertyName("_sFile")]
    public string File { get; init; } = string.Empty;

    [JsonPropertyName("_sFile220")]
    public string? File220 { get; init; }

    [JsonPropertyName("_sFile530")]
    public string? File530 { get; init; }

    [JsonPropertyName("_sFile100")]
    public string? File100 { get; init; }
}

public record ApiRootCategory
{
    [JsonPropertyName("_sName")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("_sProfileUrl")]
    public string? ProfileUrl { get; init; }

    [JsonPropertyName("_sIconUrl")]
    public string? IconUrl { get; init; }
}