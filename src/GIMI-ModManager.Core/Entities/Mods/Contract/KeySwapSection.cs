using GIMI_ModManager.Core.Entities.Mods.FileModels;

namespace GIMI_ModManager.Core.Entities.Mods.Contract;

public record KeySwapSection
{
    public string SectionName { get; init; } = "Unknown";

    public string? ForwardKey { get; init; }

    public string? BackwardKey { get; init; }

    public int? Variants { get; init; }

    public string? Type { get; init; }

    public List<string> ForwardKeys { get; init; } = [];

    public List<string> BackwardKeys { get; init; } = [];

    public string? OriginalSectionName { get; init; }
    internal static KeySwapSection FromIniKeySwapSection(IniKeySwapSection iniKeySwapSection)
    {
        return new KeySwapSection
        {
            SectionName = iniKeySwapSection.SectionKey,
            ForwardKey = string.Join(", ", iniKeySwapSection.ForwardKeys),
            BackwardKey = string.Join(", ", iniKeySwapSection.BackwardKeys),
            ForwardKeys = [.. iniKeySwapSection.ForwardKeys],
            BackwardKeys = [.. iniKeySwapSection.BackwardKeys],
            Variants = iniKeySwapSection.SwapVar?.Length,
            Type = iniKeySwapSection.Type ?? "",
        };
    }
}