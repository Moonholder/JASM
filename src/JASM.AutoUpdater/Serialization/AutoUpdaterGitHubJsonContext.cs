using System.Text.Json.Serialization;
using System.Text.Json;

namespace JASM.AutoUpdater.Serialization;

[JsonSerializable(typeof(ApiGitHubRelease[]))]
internal partial class AutoUpdaterGitHubJsonContext : JsonSerializerContext
{
}