using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace GIMI_ModManager.Core.Services.ModPresetService.JsonModels;

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global", Justification = "This is a json model")]
internal class JsonModVariantCollection
{
    public string? ActiveVariantName { get; set; }
    public List<JsonModVariantPreset> Variants { get; set; } = new();
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global", Justification = "This is a json model")]
internal class JsonModVariantPreset
{
    public required string Name { get; set; }
    public Dictionary<string, string> Preferences { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Created { get; set; }
}