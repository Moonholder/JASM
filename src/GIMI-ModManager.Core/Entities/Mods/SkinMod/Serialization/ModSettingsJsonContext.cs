using System.Text.Json.Serialization;
using System.Text.Json;
using GIMI_ModManager.Core.Entities.Mods.FileModels;

namespace GIMI_ModManager.Core.Entities.Mods.SkinMod.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JsonModSettings))]
internal partial class ModSettingsJsonContext : JsonSerializerContext
{
}