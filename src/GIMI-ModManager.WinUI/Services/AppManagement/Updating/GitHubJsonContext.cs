using System.Text.Json.Serialization;

namespace GIMI_ModManager.WinUI.Services.AppManagement.Updating;

[JsonSerializable(typeof(GitHubRelease[]))]
internal partial class GitHubJsonContext : JsonSerializerContext
{
}