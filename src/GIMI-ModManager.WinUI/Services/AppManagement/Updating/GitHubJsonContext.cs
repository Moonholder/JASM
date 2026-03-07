using System.Text.Json.Serialization;

namespace GIMI_ModManager.WinUI.Services.AppManagement.Updating;

[JsonSerializable(typeof(GitHubRelease[]))]
[JsonSerializable(typeof(GitHubReleaseAsset[]))]
internal partial class GitHubJsonContext : JsonSerializerContext
{
}