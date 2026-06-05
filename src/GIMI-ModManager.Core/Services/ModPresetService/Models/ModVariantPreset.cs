using System;
using System.Collections.Generic;

namespace GIMI_ModManager.Core.Services.ModPresetService.Models;

public class ModVariantPreset
{
    public required string Name { get; set; }
    public Dictionary<string, string> Preferences { get; set; } = new();
    public DateTime Created { get; set; } = DateTime.Now;
}