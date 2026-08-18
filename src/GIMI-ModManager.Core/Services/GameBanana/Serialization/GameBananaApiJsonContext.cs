using System.Text.Json.Serialization;
using System.Text.Json;
using GIMI_ModManager.Core.Services.GameBanana.ApiModels;

namespace GIMI_ModManager.Core.Services.GameBanana.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ApiModProfile))]
[JsonSerializable(typeof(ApiModFilesInfo))]
[JsonSerializable(typeof(ApiModFileInfo))]
[JsonSerializable(typeof(ApiAuthor))]
[JsonSerializable(typeof(ApiCategoryItem))]
[JsonSerializable(typeof(List<ApiCategoryItem>))]
[JsonSerializable(typeof(ApiModRecord))]
[JsonSerializable(typeof(ApiPaginatedResponse<ApiModRecord>))]
[JsonSerializable(typeof(ApiModSubmitter))]
[JsonSerializable(typeof(ApiPreviewImage))]
[JsonSerializable(typeof(Dictionary<string, ApiPreviewImage>))]
[JsonSerializable(typeof(Dictionary<string, List<ApiPreviewImage>>))]
[JsonSerializable(typeof(ApiRootCategory))]
[JsonSerializable(typeof(ApiPaginationMetadata))]
[JsonSerializable(typeof(ApiModUpdate))]
[JsonSerializable(typeof(ApiModUpdateChange))]
[JsonSerializable(typeof(List<ApiModUpdateChange>))]
[JsonSerializable(typeof(List<ApiModFileInfo>))]
[JsonSerializable(typeof(ApiModUpdateList))]
internal partial class GameBananaApiJsonContext : JsonSerializerContext
{
}
