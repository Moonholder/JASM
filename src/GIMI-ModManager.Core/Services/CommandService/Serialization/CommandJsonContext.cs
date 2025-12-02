using System.Text.Json.Serialization;
using System.Text.Json;
using GIMI_ModManager.Core.Services.CommandService.JsonModels;

namespace GIMI_ModManager.Core.Services.CommandService.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JsonCommandRoot))]
[JsonSerializable(typeof(JsonCommandDefinition))]
internal partial class CommandJsonContext : JsonSerializerContext
{
}