using System;
using System.Text.Json.Serialization;

namespace JASM.AutoUpdater;

public class ApiGitHubRelease
{
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("target_commitish")]
    public string? TargetCommitish { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; } = DateTime.MinValue;

    [JsonPropertyName("assets")]
    public ApiAssets[]? Assets { get; set; }

    public ApiGitHubRelease() { }
}