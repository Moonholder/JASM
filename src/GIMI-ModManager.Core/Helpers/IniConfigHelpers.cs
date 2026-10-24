using GIMI_ModManager.Core.Entities.Mods.FileModels;
using System.Text.RegularExpressions;

namespace GIMI_ModManager.Core.Helpers;

// This class just holds code that i don't know where to put yet.
public static partial class IniConfigHelpers
{
    public static IniKeySwapSection? ParseKeySwap(ICollection<string> fileLines, string sectionLine)
    {
        var skinModKeySwap = new IniKeySwapSection
        {
            SectionKey = sectionLine.Trim()
        };

        var forwardKeys = new List<string>();
        var backwardKeys = new List<string>();

        foreach (var line in fileLines)
        {
            if (IsIniKey(line, IniKeySwapSection.ForwardIniKey))
            {
                var value = GetIniValue(line);
                if (!string.IsNullOrEmpty(value))
                {
                    forwardKeys.Add(value);
                }
            }
            else if (IsIniKey(line, IniKeySwapSection.BackwardIniKey))
            {
                var value = GetIniValue(line);
                if (!string.IsNullOrEmpty(value))
                {
                    backwardKeys.Add(value);
                }
            }
            else if (IsIniKey(line, IniKeySwapSection.TypeIniKey))
                skinModKeySwap.Type = GetIniValue(line);

            else if (SwapvarRegex().IsMatch(line))
            {
                var value = GetIniValue(line);
                if (!string.IsNullOrEmpty(value))
                {
                    skinModKeySwap.SwapVar = [.. value.Split([','], StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
                        .Where(v => !string.IsNullOrEmpty(v))];
                }
            }

            else if (IsSection(line))
                break;
        }

        skinModKeySwap.ForwardKeys.AddRange(forwardKeys);
        skinModKeySwap.BackwardKeys.AddRange(backwardKeys);

        var result = skinModKeySwap.AnyValues() ? skinModKeySwap : null;
        return result;
    }

    public static string? GetIniValue(string line)
    {
        if (IsComment(line)) return null;

        var split = line.Split('=');

        if (split.Length <= 2) return split.Length != 2 ? null : split[1].Trim();


        split[1] = string.Join("=", split.Skip(1));
        return split[1].Trim();
    }

    public static string? GetIniKey(string line)
    {
        if (IsComment(line)) return null;

        var split = line.Split('=');
        return split.Length != 2 ? split.FirstOrDefault()?.Trim() : split[0].Trim();
    }

    public static bool IsComment(string line) => line.Trim().StartsWith(";");

    public static bool IsSection(string line, string? sectionKey = null)
    {
        line = line.Trim();

        if (sectionKey is null && !string.IsNullOrEmpty(line))
        {
            return line.StartsWith('[') && line.IndexOf(']') > 0;
        }

        if (!string.IsNullOrEmpty(sectionKey) && line.StartsWith('[') && line.IndexOf(']') > 0)
        {
            var bracketIndex = line.IndexOf(']');
            var sectionName = line[..(bracketIndex + 1)].Trim();
            if (sectionName.Equals($"[{sectionKey}]", StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }

            if (sectionKey.StartsWith('[') && sectionKey.EndsWith(']') && sectionKey.Equals(sectionName, StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsIniKey(string line, string key) =>
        line.Trim().StartsWith(key, StringComparison.CurrentCultureIgnoreCase);

    public static string? FormatIniKey(string key, string? value) =>
        value is not null ? $"{key} = {value}" : null;

    public static string RemoveKeyPrefixes(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        var displayName = name.Trim();

        // Match specific composite prefixes first; retain original name if exact match.
        if (displayName.StartsWith(IniKeySwapSection.CommandListSection, StringComparison.OrdinalIgnoreCase))
        {
            if (displayName.Length > IniKeySwapSection.CommandListSection.Length)
                displayName = displayName[IniKeySwapSection.CommandListSection.Length..];
            else
                return displayName;
        }
#pragma warning disable CS0618
        else if (displayName.StartsWith(IniKeySwapSection.CommandListSectionTypo, StringComparison.OrdinalIgnoreCase))
        {
            if (displayName.Length > IniKeySwapSection.CommandListSectionTypo.Length)
                displayName = displayName[IniKeySwapSection.CommandListSectionTypo.Length..];
            else
                return displayName;
        }
#pragma warning restore CS0618
        else if (displayName.StartsWith(IniKeySwapSection.KeySwapIniSection, StringComparison.OrdinalIgnoreCase))
        {
            if (displayName.Length > IniKeySwapSection.KeySwapIniSection.Length)
                displayName = displayName[IniKeySwapSection.KeySwapIniSection.Length..];
            else
                return displayName;
        }
        else if (displayName.StartsWith(IniKeySwapSection.ForwardIniKey, StringComparison.OrdinalIgnoreCase))
        {
            if (displayName.Length > IniKeySwapSection.ForwardIniKey.Length)
                displayName = displayName[IniKeySwapSection.ForwardIniKey.Length..];
            else
                return displayName;
        }

        // Strip any remaining leading delimiters.
        displayName = displayName.TrimStart(' ', '_', '-');

        return string.IsNullOrWhiteSpace(displayName) ? name.Trim() : displayName;
    }

    public static string GetSectionPrefix(string sectionName, string currentDisplay)
    {
        if (!string.IsNullOrEmpty(currentDisplay) &&
            sectionName.EndsWith(currentDisplay, StringComparison.OrdinalIgnoreCase) &&
            sectionName.Length > currentDisplay.Length)
        {
            return sectionName[..^currentDisplay.Length];
        }

        if (sectionName.StartsWith(IniKeySwapSection.CommandListSection, StringComparison.OrdinalIgnoreCase))
            return sectionName[..IniKeySwapSection.CommandListSection.Length];
#pragma warning disable CS0618
        if (sectionName.StartsWith(IniKeySwapSection.CommandListSectionTypo, StringComparison.OrdinalIgnoreCase))
            return sectionName[..IniKeySwapSection.CommandListSectionTypo.Length];
#pragma warning restore CS0618
        if (sectionName.StartsWith(IniKeySwapSection.KeySwapIniSection, StringComparison.OrdinalIgnoreCase))
            return sectionName[..IniKeySwapSection.KeySwapIniSection.Length];
        if (sectionName.StartsWith(IniKeySwapSection.ForwardIniKey, StringComparison.OrdinalIgnoreCase))
            return sectionName[..IniKeySwapSection.ForwardIniKey.Length];

        return "Key";
    }

    public static bool IsBackKeySupported(string? type, int variationsCount)
    {
        return string.Equals(type?.Trim(), "cycle", StringComparison.OrdinalIgnoreCase) && variationsCount > 1;
    }

    public static string GetModeDisplayText(string? type, int variationsCount, bool isChinese)
    {
        if (string.IsNullOrWhiteSpace(type))
            return string.Empty;

        var typeLower = type.Trim().ToLowerInvariant();

        if (typeLower == "cycle")
        {
            if (variationsCount <= 1)
                return string.Empty;

            return isChinese ? $"{variationsCount} 项" : $"{variationsCount} items";
        }

        if (typeLower == "toggle")
        {
            return isChinese ? "开关" : "Toggle";
        }

        if (typeLower == "hold")
        {
            return isChinese ? "长按" : "Hold";
        }

        if (typeLower == "press")
        {
            return isChinese ? "按压" : "Press";
        }

        return type.Trim();
    }

    public static string GetModeToolTipText(string? type, int variationsCount, bool isChinese)
    {
        if (string.IsNullOrWhiteSpace(type))
            return string.Empty;

        var typeLower = type.Trim().ToLowerInvariant();

        if (typeLower == "cycle")
        {
            if (variationsCount <= 1)
                return string.Empty;

            return isChinese
                ? $"循环切换模式 (共 {variationsCount} 项)"
                : $"Cycle mode ({variationsCount} items)";
        }

        if (typeLower == "toggle")
        {
            return isChinese ? "开关模式 (切换开启/关闭)" : "Toggle mode (turn on/off)";
        }

        if (typeLower == "hold")
        {
            return isChinese ? "长按模式 (按住生效)" : "Hold mode (active while held)";
        }

        if (typeLower == "press")
        {
            return isChinese ? "按压模式 (按下触发)" : "Press mode (trigger on press)";
        }

        return type.Trim();
    }

    public static string GetActionLabel(bool isBackKeySupported, bool isChinese)
    {
        return isBackKeySupported
            ? (isChinese ? "切换" : "Switch")
            : (isChinese ? "按键" : "Key");
    }

    public static string GetBackActionLabel(bool isChinese)
    {
        return isChinese ? "反向" : "Back";
    }

    [GeneratedRegex(@"^\s*\$\w+\s*=\s*[-\d]+\s*(,\s*[-\d]+\s*)*")]
    private static partial Regex SwapvarRegex();
}