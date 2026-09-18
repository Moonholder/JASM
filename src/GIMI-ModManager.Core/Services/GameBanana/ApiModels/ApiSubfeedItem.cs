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

    [JsonPropertyName("_aPreviewContent")]
    public Dictionary<string, ApiPreviewImage?>? PreviewContent { get; init; }

    [JsonPropertyName("_bHasContentRatings")]
    public bool HasContentRatings { get; init; }

    [JsonPropertyName("_aTags")]
    public List<string> Tags { get; init; } = new();

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
    /// 从第一张预览图计算缩略图 URL。优先使用 530px 变体以提升清晰度。
    /// </summary>
    public string? ThumbnailUrl
    {
        get
        {
            if (PreviewContent == null || !PreviewContent.TryGetValue("screenshot", out var img) || img == null)
                return null;
            var file = img.File530 ?? img.File220 ?? img.File;
            return string.IsNullOrEmpty(file) ? null : $"{img.BaseUrl}/{file}";
        }
    }

    /// <summary>
    /// 预览图数量（从 _aImages 数组长度获取）。
    /// </summary>
    /// <summary>
    /// Safe-for-work (blurred) thumbnail URL provided by GameBanana for content
    /// rated submissions with initial visibility "warn".
    /// </summary>
    public string? SfwThumbnailUrl
    {
        get
        {
            if (PreviewContent == null || !PreviewContent.TryGetValue("screenshot", out var img) || img == null)
                return null;
            var file = img.File220Sfw;
            return string.IsNullOrEmpty(file) ? null : $"{img.BaseUrl}/{file}";
        }
    }

    /// <summary>
    /// Approximate thumbnail aspect ratio (width / height) derived from the v13
    /// height hints; the widths are encoded in the field names (220/530).
    /// </summary>
    public double ThumbnailAspectRatio
    {
        get
        {
            var img = PreviewContent?.GetValueOrDefault("screenshot");
            if (img is not null && !string.IsNullOrEmpty(img.File530) && img.Height530 > 0)
                return 530.0 / img.Height530;
            if (img is not null && !string.IsNullOrEmpty(img.File220) && img.Height220 > 0)
                return 220.0 / img.Height220;
            return 16.0 / 9.0;
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

    [JsonPropertyName("_sFile530Sfw")]
    public string? File530Sfw { get; init; }

    [JsonPropertyName("_sFile220Sfw")]
    public string? File220Sfw { get; init; }

    [JsonPropertyName("_hFile100")]
    public int Height100 { get; init; }

    [JsonPropertyName("_hFile220")]
    public int Height220 { get; init; }

    [JsonPropertyName("_hFile530")]
    public int Height530 { get; init; }

    [JsonPropertyName("_sCaption")]
    public string? Caption { get; init; }
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