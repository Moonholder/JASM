using System.Text.Json.Serialization;
using System.Text.Json;
using GIMI_ModManager.Core.Services.GameBanana.ApiModels;

namespace GIMI_ModManager.Core.Services.GameBanana.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ApiModProfile))]
[JsonSerializable(typeof(ApiModFilesInfo))]
[JsonSerializable(typeof(ApiModFileInfo))]
[JsonSerializable(typeof(ApiAuthor))]
[JsonSerializable(typeof(ApiImagesRoot))]
[JsonSerializable(typeof(ApiImageUrl))]
internal partial class GameBananaApiJsonContext : JsonSerializerContext
{
}