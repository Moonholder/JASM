namespace GIMI_ModManager.Core.Entities.Mods.FileModels;

public class IniKeySwapSection
{
    public Dictionary<string, string> IniKeyValues { get; } = new();

    public List<string> ForwardKeys { get; set; } = [];
    public List<string> BackwardKeys { get; set; } = [];

    public const string KeySwapIniSection = "KeySwap";

    public const string CommandListSection = "KeyCommndList";
    public string SectionKey { get; set; } = KeySwapIniSection;

    public const string ForwardIniKey = "key";

    public string? ForwardHotkey
    {
        get => ForwardKeys.Count > 0 ? string.Join(", ", ForwardKeys) : null;
        set
        {
            ForwardKeys.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                ForwardKeys.AddRange(value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim())
                    .Where(k => !string.IsNullOrEmpty(k)));
            }
        }
    }

    public const string BackwardIniKey = "back";

    public string? BackwardHotkey
    {
        get => BackwardKeys.Count > 0 ? string.Join(", ", BackwardKeys) : null;
        set
        {
            BackwardKeys.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                BackwardKeys.AddRange(value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
                    .Select(k => k.Trim())
                    .Where(k => !string.IsNullOrEmpty(k)));
            }
        }
    }

    public const string TypeIniKey = "type";

    public string? Type
    {
        get => IniKeyValues.GetValueOrDefault(TypeIniKey);
        set => IniKeyValues[TypeIniKey] = value ?? string.Empty;
    }

    public const string SwapVarIniKey = "$swapvar";
    public string[]? SwapVar { get; set; }

    public bool AnyValues()
    {
        return ForwardKeys.Count > 0 || BackwardKeys.Count > 0;
    }
}