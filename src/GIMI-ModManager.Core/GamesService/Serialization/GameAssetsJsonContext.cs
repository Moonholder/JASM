using System.Text.Json.Serialization;
using System.Text.Json;
using GIMI_ModManager.Core.GamesService.JsonModels;
using System.Collections.Generic;

namespace GIMI_ModManager.Core.GamesService.Serialization;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(JsonGame))]
[JsonSerializable(typeof(List<JsonGame>))]
[JsonSerializable(typeof(JsonRegion))]
[JsonSerializable(typeof(List<JsonRegion>))]
[JsonSerializable(typeof(JsonCharacter))]
[JsonSerializable(typeof(List<JsonCharacter>))]
[JsonSerializable(typeof(JsonNpc))]
[JsonSerializable(typeof(List<JsonNpc>))]
[JsonSerializable(typeof(JsonWeapon))]
[JsonSerializable(typeof(List<JsonWeapon>))]
[JsonSerializable(typeof(JsonClasses))]
[JsonSerializable(typeof(List<JsonClasses>))]
[JsonSerializable(typeof(GIMI_ModManager.Core.GamesService.JsonModels.JsonElement))]
[JsonSerializable(typeof(List<GIMI_ModManager.Core.GamesService.JsonModels.JsonElement>))]
[JsonSerializable(typeof(JsonBaseNameable))]
[JsonSerializable(typeof(List<JsonBaseNameable>))]
[JsonSerializable(typeof(JsonBaseModdableObject))]
[JsonSerializable(typeof(List<JsonBaseModdableObject>))]
[JsonSerializable(typeof(JsonCustom))]
[JsonSerializable(typeof(List<JsonCustom>))]
[JsonSerializable(typeof(JsonOverride))]
[JsonSerializable(typeof(List<JsonOverride>))]
[JsonSerializable(typeof(JsonCharacterSkin))]
[JsonSerializable(typeof(List<JsonCharacterSkin>))]
internal partial class GameAssetsJsonContext : JsonSerializerContext
{
}