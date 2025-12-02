using System.Text.Json.Serialization;
using System.Text.Json;
using GIMI_ModManager.Core.Services.ModPresetService.JsonModels;
using System.Collections.Generic;

namespace GIMI_ModManager.Core.Services.ModPresetService.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(JsonModPreset))]
[JsonSerializable(typeof(JsonModPresetEntry))]
internal partial class ModPresetJsonContext : JsonSerializerContext
{
}