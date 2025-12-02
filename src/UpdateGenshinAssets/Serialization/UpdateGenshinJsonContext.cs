using System.Text.Json.Serialization;
using System.Text.Json;
using UpdateGenshinAssets;
using GIMI_ModManager.Core.GamesService.JsonModels;
using System.Collections.Generic;

namespace UpdateGenshinAssets.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(JsonWeaponRoot))]
[JsonSerializable(typeof(Datum))]
[JsonSerializable(typeof(List<JsonWeapon>))]
internal partial class UpdateGenshinJsonContext : JsonSerializerContext
{
}
