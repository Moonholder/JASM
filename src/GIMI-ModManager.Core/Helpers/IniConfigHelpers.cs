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
    [GeneratedRegex(@"^\s*\$\w+\s*=\s*[-\d]+\s*(,\s*[-\d]+\s*)*")]
    private static partial Regex SwapvarRegex();
}