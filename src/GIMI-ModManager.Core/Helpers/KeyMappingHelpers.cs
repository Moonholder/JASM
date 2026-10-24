using System.Globalization;
using System.Text.RegularExpressions;

namespace GIMI_ModManager.Core.Helpers;

/// <summary>
/// <summary>Key mapping and friendly display formatting helpers.</summary>
/// </summary>
public static partial class KeyMappingHelpers
{
    private static readonly Dictionary<string, string> EnglishMappings;
    private static readonly Dictionary<string, string> ChineseMappings;

    static KeyMappingHelpers()
    {
        var common = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Directional keys
            { "VK_UP", "↑" },
            { "VK_DOWN", "↓" },
            { "VK_LEFT", "←" },
            { "VK_RIGHT", "→" },

            // Function keys
            { "VK_F1", "F1" }, { "VK_F2", "F2" }, { "VK_F3", "F3" }, { "VK_F4", "F4" },
            { "VK_F5", "F5" }, { "VK_F6", "F6" }, { "VK_F7", "F7" }, { "VK_F8", "F8" },
            { "VK_F9", "F9" }, { "VK_F10", "F10" }, { "VK_F11", "F11" }, { "VK_F12", "F12" },

            // Number keys
            { "VK_0", "0" }, { "VK_1", "1" }, { "VK_2", "2" }, { "VK_3", "3" },
            { "VK_4", "4" }, { "VK_5", "5" }, { "VK_6", "6" }, { "VK_7", "7" },
            { "VK_8", "8" }, { "VK_9", "9" },
            { "0", "0" }, { "1", "1" }, { "2", "2" }, { "3", "3" },
            { "4", "4" }, { "5", "5" }, { "6", "6" }, { "7", "7" },
            { "8", "8" }, { "9", "9" },

            // Letter keys
            { "VK_A", "A" }, { "VK_B", "B" }, { "VK_C", "C" }, { "VK_D", "D" },
            { "VK_E", "E" }, { "VK_F", "F" }, { "VK_G", "G" }, { "VK_H", "H" },
            { "VK_I", "I" }, { "VK_J", "J" }, { "VK_K", "K" }, { "VK_L", "L" },
            { "VK_M", "M" }, { "VK_N", "N" }, { "VK_O", "O" }, { "VK_P", "P" },
            { "VK_Q", "Q" }, { "VK_R", "R" }, { "VK_S", "S" }, { "VK_T", "T" },
            { "VK_U", "U" }, { "VK_V", "V" }, { "VK_W", "W" }, { "VK_X", "X" },
            { "VK_Y", "Y" }, { "VK_Z", "Z" },

            // Navigation and control keys
            { "VK_ESCAPE", "Esc" },
            { "VK_TAB", "Tab" },
            { "VK_CAPITAL", "Caps Lock" },
            { "VK_SHIFT", "Shift" },
            { "VK_CONTROL", "Ctrl" },
            { "VK_MENU", "Alt" },
            { "VK_BACK", "Backspace" },
            { "VK_Backspace", "Backspace" },
            { "VK_DELETE", "Delete" },
            { "VK_INSERT", "Insert" },
            { "VK_HOME", "Home" },
            { "VK_END", "End" },
            { "VK_PRIOR", "Page Up" },
            { "VK_NEXT", "Page Down" },
            { "VK_SLASH", "/" },
            { "VK_COMMA", "," },
            { "VK_TILDE", "~" },
            { "VK_PERIOD", "." },
            { "VK_OEM_1", ";" },
            { "VK_OEM_2", "?" },
            { "VK_OEM_3", "`" },
            { "VK_OEM_4", "[" },
            { "VK_OEM_5", "\\" },
            { "VK_OEM_6", "]" },
            { "VK_OEM_7", "'" },
            { "VK_OEM_PLUS", "=" },
            { "VK_OEM_MINUS", "-" },
            { "VK_OEM_PERIOD", "." },

            // Stick presses and aliases
            { "XB_LEFT_THUMB_PRESS", "L3" },
            { "XB_RIGHT_THUMB_PRESS", "R3" },
            { "XB_LEFT_STICK_PRESS", "L3" },
            { "XB_RIGHT_STICK_PRESS", "R3" },
            { "WIN", "Win" }
        };

        // English entries
        EnglishMappings = new Dictionary<string, string>(common, StringComparer.OrdinalIgnoreCase)
        {
            { "VK_SPACE", "Space" },
            { "VK_RETURN", "Enter" },
            { "VK_OEM_COMMA", "," },
            { ",", "," },
            { "，", "," },

            // Numpad
            { "VK_NUMPAD0", "Numpad 0" }, { "VK_NUMPAD1", "Numpad 1" }, { "VK_NUMPAD2", "Numpad 2" },
            { "VK_NUMPAD3", "Numpad 3" }, { "VK_NUMPAD4", "Numpad 4" }, { "VK_NUMPAD5", "Numpad 5" },
            { "VK_NUMPAD6", "Numpad 6" }, { "VK_NUMPAD7", "Numpad 7" }, { "VK_NUMPAD8", "Numpad 8" },
            { "VK_NUMPAD9", "Numpad 9" },
            { "VK_MULTIPLY", "Numpad *" }, { "VK_ADD", "Numpad +" }, { "VK_SUBTRACT", "Numpad -" },
            { "VK_DECIMAL", "Numpad ." }, { "VK_DIVIDE", "Numpad /" },
            { "NUMPAD0", "Numpad 0" }, { "NUMPAD1", "Numpad 1" }, { "NUMPAD2", "Numpad 2" },
            { "NUMPAD3", "Numpad 3" }, { "NUMPAD4", "Numpad 4" }, { "NUMPAD5", "Numpad 5" },
            { "NUMPAD6", "Numpad 6" }, { "NUMPAD7", "Numpad 7" }, { "NUMPAD8", "Numpad 8" },
            { "NUMPAD9", "Numpad 9" },
            { "MULTIPLY", "Numpad *" }, { "ADD", "Numpad +" }, { "SUBTRACT", "Numpad -" },
            { "DECIMAL", "Numpad ." }, { "DIVIDE", "Numpad /" },

            // Mouse
            { "VK_LBUTTON", "Left Mouse" }, { "VK_RBUTTON", "Right Mouse" }, { "VK_MBUTTON", "Middle Mouse" },
            { "VK_XBUTTON1", "Mouse XButton 1" }, { "VK_XBUTTON2", "Mouse XButton 2" },
            { "WHEELUP", "Wheel Up" }, { "WHEELDOWN", "Wheel Down" },

            // Gamepad
            { "XB_A", "Gamepad A" }, { "XB_B", "Gamepad B" }, { "XB_X", "Gamepad X" }, { "XB_Y", "Gamepad Y" },
            { "XB_RIGHT_SHOULDER", "Gamepad RB" }, { "XB_LEFT_SHOULDER", "Gamepad LB" },
            { "XB_RIGHT_TRIGGER", "Gamepad RT" }, { "XB_LEFT_TRIGGER", "Gamepad LT" },
            { "XB_DPAD_UP", "Gamepad ↑" }, { "XB_DPAD_DOWN", "Gamepad ↓" },
            { "XB_DPAD_LEFT", "Gamepad ←" }, { "XB_DPAD_RIGHT", "Gamepad →" },
            { "XB_START", "Gamepad Start" }, { "XB_BACK", "Gamepad Back" }, { "XB_GUIDE", "Gamepad Guide" },
            { "XB_LEFT_THUMB", "Left Stick" }, { "XB_RIGHT_THUMB", "Right Stick" },

            // Modifiers
            { "MODIFIERS", "Modifiers" }, { "ALT", "Alt" }, { "SHIFT", "Shift" }, { "CTRL", "Ctrl" },
            { "RCTRL", "Right Ctrl" }, { "VK_LSHIFT", "Left Shift" }, { "VK_RSHIFT", "Right Shift" },
            { "VK_LCONTROL", "Left Ctrl" }, { "VK_RCONTROL", "Right Ctrl" },
            { "VK_LMENU", "Left Alt" }, { "VK_RMENU", "Right Alt" },
            { "VK_LWIN", "Left Win" }, { "VK_RWIN", "Right Win" }
        };

        // Chinese entries
        ChineseMappings = new Dictionary<string, string>(common, StringComparer.OrdinalIgnoreCase)
        {
            { "VK_SPACE", "空格" },
            { "VK_RETURN", "回车" },
            { "VK_OEM_COMMA", "逗号" },
            { ",", "逗号" },
            { "，", "逗号" },

            // Numpad
            { "VK_NUMPAD0", "小键盘 0" }, { "VK_NUMPAD1", "小键盘 1" }, { "VK_NUMPAD2", "小键盘 2" },
            { "VK_NUMPAD3", "小键盘 3" }, { "VK_NUMPAD4", "小键盘 4" }, { "VK_NUMPAD5", "小键盘 5" },
            { "VK_NUMPAD6", "小键盘 6" }, { "VK_NUMPAD7", "小键盘 7" }, { "VK_NUMPAD8", "小键盘 8" },
            { "VK_NUMPAD9", "小键盘 9" },
            { "VK_MULTIPLY", "小键盘 *" }, { "VK_ADD", "小键盘 +" }, { "VK_SUBTRACT", "小键盘 -" },
            { "VK_DECIMAL", "小键盘 ." }, { "VK_DIVIDE", "小键盘 /" },
            { "NUMPAD0", "小键盘 0" }, { "NUMPAD1", "小键盘 1" }, { "NUMPAD2", "小键盘 2" },
            { "NUMPAD3", "小键盘 3" }, { "NUMPAD4", "小键盘 4" }, { "NUMPAD5", "小键盘 5" },
            { "NUMPAD6", "小键盘 6" }, { "NUMPAD7", "小键盘 7" }, { "NUMPAD8", "小键盘 8" },
            { "NUMPAD9", "小键盘 9" },
            { "MULTIPLY", "小键盘 *" }, { "ADD", "小键盘 +" }, { "SUBTRACT", "小键盘 -" },
            { "DECIMAL", "小键盘 ." }, { "DIVIDE", "小键盘 /" },

            // Mouse
            { "VK_LBUTTON", "鼠标左键" }, { "VK_RBUTTON", "鼠标右键" }, { "VK_MBUTTON", "鼠标中键" },
            { "VK_XBUTTON1", "鼠标侧键 1" }, { "VK_XBUTTON2", "鼠标侧键 2" },
            { "WHEELUP", "滚轮向上" }, { "WHEELDOWN", "滚轮向下" },

            // Gamepad
            { "XB_A", "手柄 A" }, { "XB_B", "手柄 B" }, { "XB_X", "手柄 X" }, { "XB_Y", "手柄 Y" },
            { "XB_RIGHT_SHOULDER", "手柄 RB" }, { "XB_LEFT_SHOULDER", "手柄 LB" },
            { "XB_RIGHT_TRIGGER", "手柄 RT" }, { "XB_LEFT_TRIGGER", "手柄 LT" },
            { "XB_DPAD_UP", "手柄 ↑" }, { "XB_DPAD_DOWN", "手柄 ↓" },
            { "XB_DPAD_LEFT", "手柄 ←" }, { "XB_DPAD_RIGHT", "手柄 →" },
            { "XB_START", "手柄 Start" }, { "XB_BACK", "手柄 Back" }, { "XB_GUIDE", "手柄 Guide" },
            { "XB_LEFT_THUMB", "手柄 左摇杆" }, { "XB_RIGHT_THUMB", "手柄 右摇杆" },

            // Modifiers
            { "MODIFIERS", "修饰键" }, { "ALT", "Alt" }, { "SHIFT", "Shift" }, { "CTRL", "Ctrl" },
            { "RCTRL", "右Ctrl" }, { "VK_LSHIFT", "左 Shift" }, { "VK_RSHIFT", "右 Shift" },
            { "VK_LCONTROL", "左 Ctrl" }, { "VK_RCONTROL", "右 Ctrl" },
            { "VK_LMENU", "左 Alt" }, { "VK_RMENU", "右 Alt" },
            { "VK_LWIN", "左 Win" }, { "VK_RWIN", "右 Win" }
        };
    }

    [GeneratedRegex(@"(?<=[^,，\s])[,，]")]
    public static partial Regex SeparatorCommaRegexGenerated();

    [GeneratedRegex(@"^num\s+\d+$", RegexOptions.IgnoreCase)]
    public static partial Regex NumFormatRegexGenerated();

    [GeneratedRegex(@"num\s+\d+|[^\s]+", RegexOptions.IgnoreCase)]
    public static partial Regex TokenRegexGenerated();

    public static readonly Regex SeparatorCommaRegex = SeparatorCommaRegexGenerated();
    public static readonly Regex NumFormatRegex = NumFormatRegexGenerated();

    public static bool IsNumFormat(string keyText) => NumFormatRegex.IsMatch(keyText.Trim());

    public static bool IsAllCommasWithValidSpace(string input)
    {
        var trimmed = input.Trim();
        return trimmed.All(c => c is ',' or ' ' or '，') &&
               trimmed.Split([' '], StringSplitOptions.RemoveEmptyEntries)
                   .All(p => p.All(c => c is ',' or '，'));
    }

    /// <summary>
    /// <summary>Splits comma-separated key text into individual key tokens.</summary>
    /// </summary>
    public static List<string> SplitMultiKeys(string? value)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
            return result;

        var keyText = value.Trim();
        var parts = SeparatorCommaRegex.Split(keyText)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        foreach (var part in parts)
        {
            if (IsNumFormat(part))
            {
                result.Add(part);
            }
            else if (part.Contains(' '))
            {
                var validSubParts = TokenRegexGenerated().Matches(part)
                    .Select(m => m.Value.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();

                if (validSubParts.Count > 0)
                {
                    result.Add(string.Join(" ", validSubParts));
                }
            }
            else
            {
                result.Add(part);
            }
        }

        return result;
    }

    /// <summary>
    /// <summary>Normalizes raw key lists into individual key tokens.</summary>
    /// </summary>
    public static List<string> NormalizeKeys(IEnumerable<string>? keys)
    {
        var result = new List<string>();
        if (keys == null)
            return result;

        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var split = SplitMultiKeys(key);
            if (split.Count > 0)
            {
                result.AddRange(split);
            }
            else
            {
                var trimmed = key.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    result.Add(trimmed);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// <summary>Compares two key sequences for case-insensitive equivalence.</summary>
    /// </summary>
    public static bool AreKeyListsEqual(IEnumerable<string>? first, IEnumerable<string>? second)
    {
        if (first == null && second == null) return true;
        if (first == null || second == null) return false;

        var list1 = first.Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k)).ToList();
        var list2 = second.Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k)).ToList();

        if (list1.Count != list2.Count) return false;

        for (var i = 0; i < list1.Count; i++)
        {
            if (!string.Equals(list1[i], list2[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// <summary>Formats a sequence of keys into a comma-separated string.</summary>
    /// </summary>
    public static string FormatKeyList(IEnumerable<string>? keys)
    {
        if (keys == null)
            return string.Empty;

        var validKeys = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim());
        return string.Join(", ", validKeys);
    }

    /// <summary>
    /// <summary>Converts raw key tokens to localized display text.</summary>
    /// </summary>
    public static string ConvertToFriendlyText(string? value, bool? isChinese = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var useChinese = isChinese ?? CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        var keyText = value.Trim();
        var orSeparator = useChinese ? " 或 " : " or ";

        var orParts = SeparatorCommaRegex.Split(keyText)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        var friendlyOrParts = orParts.Select(part => ProcessValidCombinationKey(part, useChinese))
            .Where(processedPart => !string.IsNullOrEmpty(processedPart))
            .ToList();

        if (friendlyOrParts.Count == 0 && IsAllCommasWithValidSpace(keyText))
            return GetFriendlyText(",", useChinese);

        return string.Join(orSeparator, friendlyOrParts);
    }

    private static string ProcessValidCombinationKey(string keyPart, bool isChinese)
    {
        if (string.IsNullOrWhiteSpace(keyPart))
            return string.Empty;

        var tokens = TokenRegexGenerated().Matches(keyPart)
            .Select(m => m.Value.Trim())
            .Where(p => !string.IsNullOrEmpty(p) &&
                        (p == "," || !p.StartsWith("NO_", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (tokens.Count == 0)
            return string.Empty;

        var friendlyTokens = tokens.Select(p => GetFriendlyText(p, isChinese)).ToList();

        if (friendlyTokens.Count > 1)
        {
            var padPrefix = isChinese ? "手柄 " : "Gamepad ";
            if (friendlyTokens.Any(t => t.StartsWith(padPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                var first = true;
                for (var i = 0; i < friendlyTokens.Count; i++)
                {
                    if (friendlyTokens[i].StartsWith(padPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!first)
                        {
                            friendlyTokens[i] = friendlyTokens[i][padPrefix.Length..];
                        }
                        first = false;
                    }
                }
            }

            var mousePrefix = isChinese ? "鼠标" : "Mouse ";
            if (friendlyTokens.Any(t => t.StartsWith(mousePrefix, StringComparison.OrdinalIgnoreCase)))
            {
                var first = true;
                for (var i = 0; i < friendlyTokens.Count; i++)
                {
                    if (friendlyTokens[i].StartsWith(mousePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!first)
                        {
                            friendlyTokens[i] = friendlyTokens[i][mousePrefix.Length..];
                        }
                        first = false;
                    }
                }
            }
        }

        return string.Join(" + ", friendlyTokens);
    }

    private static string GetFriendlyText(string keyText, bool isChinese)
    {
        if (string.IsNullOrWhiteSpace(keyText))
            return string.Empty;

        var trimmedKey = keyText.Trim();
        var mappings = isChinese ? ChineseMappings : EnglishMappings;

        if (NumFormatRegex.IsMatch(trimmedKey))
        {
            var digit = Regex.Match(trimmedKey, @"\d+").Value;
            return isChinese ? $"小键盘 {digit}" : $"Numpad {digit}";
        }

        if (mappings.TryGetValue(trimmedKey, out var friendlyText))
            return friendlyText;

        if (!trimmedKey.StartsWith("VK_", StringComparison.OrdinalIgnoreCase))
        {
            if (mappings.TryGetValue("VK_" + trimmedKey, out friendlyText))
                return friendlyText;
        }
        else
        {
            if (mappings.TryGetValue(trimmedKey[3..], out friendlyText))
                return friendlyText;
        }

        return trimmedKey;
    }

    /// <summary>
    /// <summary>Converts virtual key codes to standard GIMI key representations.</summary>
    /// </summary>
    public static string? ConvertVirtualKeyToGimiKey(int virtualKey)
    {
        // 0-9
        if (virtualKey >= 0x30 && virtualKey <= 0x39)
            return "VK_" + (char)virtualKey;

        // A-Z
        if (virtualKey >= 0x41 && virtualKey <= 0x5A)
            return "VK_" + (char)virtualKey;

        // F1-F12
        if (virtualKey >= 0x70 && virtualKey <= 0x7B)
            return "VK_F" + (virtualKey - 0x70 + 1);

        // Numpad digits 0-9
        if (virtualKey >= 0x60 && virtualKey <= 0x69)
            return "VK_NUMPAD" + (virtualKey - 0x60);

        return virtualKey switch
        {
            // OEM Keys
            192 => "VK_OEM_3",    // ` ~
            189 => "VK_OEM_MINUS",// - _
            187 => "VK_OEM_PLUS", // = +
            186 => "VK_OEM_1",    // ; :
            191 => "VK_OEM_2",    // / ?
            219 => "VK_OEM_4",    // [ {
            220 => "VK_OEM_5",    // \ |
            221 => "VK_OEM_6",    // ] }
            222 => "VK_OEM_7",    // ' "
            188 => "VK_OEM_COMMA",// , <
            190 => "VK_OEM_PERIOD",// . >

            // Directional keys
            38 => "VK_UP",
            40 => "VK_DOWN",
            37 => "VK_LEFT",
            39 => "VK_RIGHT",

            // Common keys
            32 => "VK_SPACE",
            13 => "VK_RETURN",
            8 => "VK_BACK",
            9 => "VK_TAB",
            27 => "VK_ESCAPE",
            20 => "VK_CAPITAL",
            46 => "VK_DELETE",
            45 => "VK_INSERT",
            36 => "VK_HOME",
            35 => "VK_END",
            33 => "VK_PRIOR",
            34 => "VK_NEXT",

            // Numpad operators
            106 => "VK_MULTIPLY",
            107 => "VK_ADD",
            109 => "VK_SUBTRACT",
            110 => "VK_DECIMAL",
            111 => "VK_DIVIDE",

            _ => null
        };
    }

    /// <summary>
    /// <summary>Converts virtual key codes to 3dmigoto native input tokens.</summary>
    /// </summary>
    /// <summary>
    /// Converts virtual key codes to 3dmigoto native input tokens, disambiguating physical numpad keys using scan code and extended key flag.
    /// </summary>
    public static string ConvertVirtualKeyToGimiInputKey(int virtualKey, uint scanCode, bool isExtendedKey)
    {
        if (!isExtendedKey && scanCode != 0)
        {
            switch (scanCode)
            {
                case 0x52: return "NUMPAD0";
                case 0x4F: return "NUMPAD1";
                case 0x50: return "NUMPAD2";
                case 0x51: return "NUMPAD3";
                case 0x4B: return "NUMPAD4";
                case 0x4C: return "NUMPAD5";
                case 0x4D: return "NUMPAD6";
                case 0x47: return "NUMPAD7";
                case 0x48: return "NUMPAD8";
                case 0x49: return "NUMPAD9";
                case 0x53: return "DECIMAL";
            }
        }

        return ConvertVirtualKeyToGimiInputKey(virtualKey);
    }

    public static string ConvertVirtualKeyToGimiInputKey(int virtualKey)
    {
        // Number row 0-9
        if (virtualKey >= 0x30 && virtualKey <= 0x39)
            return ((char)virtualKey).ToString();

        // Numpad digits 0-9
        if (virtualKey >= 0x60 && virtualKey <= 0x69)
            return "NUMPAD" + (virtualKey - 0x60);

        // Numpad operators
        if (virtualKey == 106) return "MULTIPLY";
        if (virtualKey == 107) return "ADD";
        if (virtualKey == 109) return "SUBTRACT";
        if (virtualKey == 110) return "DECIMAL";
        if (virtualKey == 111) return "DIVIDE";

        // Modifiers normalized to 3dmigoto standards
        if (virtualKey is 17 or 162 or 163) return "VK_CONTROL";
        if (virtualKey is 16 or 160 or 161) return "VK_SHIFT";
        if (virtualKey is 18 or 164 or 165) return "VK_MENU";

        // Letter keys A-Z
        if (virtualKey >= 0x41 && virtualKey <= 0x5A)
            return "VK_" + (char)virtualKey;

        // Function keys F1-F12
        if (virtualKey >= 0x70 && virtualKey <= 0x7B)
            return "VK_F" + (virtualKey - 0x70 + 1);

        return virtualKey switch
        {
            // OEM punctuation
            186 => "VK_OEM_1",
            187 => "VK_OEM_PLUS",
            188 => "VK_OEM_COMMA",
            189 => "VK_OEM_MINUS",
            190 => "VK_OEM_PERIOD",
            191 => "VK_OEM_2",
            192 => "VK_OEM_3",
            219 => "VK_OEM_4",
            220 => "VK_OEM_5",
            221 => "VK_OEM_6",
            222 => "VK_OEM_7",

            // Navigation and control keys
            38 => "VK_UP",
            40 => "VK_DOWN",
            37 => "VK_LEFT",
            39 => "VK_RIGHT",
            32 => "VK_SPACE",
            13 => "VK_RETURN",
            8 => "VK_BACK",
            9 => "VK_TAB",
            20 => "VK_CAPITAL",
            45 => "VK_INSERT",
            46 => "VK_DELETE",
            36 => "VK_HOME",
            35 => "VK_END",
            33 => "VK_PRIOR",
            34 => "VK_NEXT",

            _ => "VK_" + virtualKey
        };
    }

    /// <summary>
    /// <summary>Builds a standard combination key string.</summary>
    /// </summary>
    public static string BuildComboKey(bool ctrl, bool shift, bool alt, string? primaryKey)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("VK_CONTROL");
        if (shift) parts.Add("VK_SHIFT");
        if (alt) parts.Add("VK_MENU");

        if (!string.IsNullOrWhiteSpace(primaryKey))
        {
            var trimmed = primaryKey.Trim();

            // Normalize numpad tokens to prevent 3dmigoto parsing failures.
            if (IsNumFormat(trimmed))
            {
                var digit = Regex.Match(trimmed, @"\d+").Value;
                trimmed = "VK_NUMPAD" + digit;
            }
            else if (trimmed.StartsWith("numpad", StringComparison.OrdinalIgnoreCase) &&
                     trimmed.Length > 6 &&
                     char.IsAsciiDigit(trimmed[6]))
            {
                trimmed = "VK_NUMPAD" + trimmed[6..];
            }

            if (!parts.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                parts.Add(trimmed);
            }
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// <summary>Deconstructs a combination key string into modifiers and a primary key.</summary>
    /// </summary>
    public static (bool ctrl, bool shift, bool alt, string primaryKey) DeconstructComboKey(string? comboKey)
    {
        if (string.IsNullOrWhiteSpace(comboKey))
            return (false, false, false, string.Empty);

        var tokens = TokenRegexGenerated().Matches(comboKey)
            .Select(m => m.Value.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        bool ctrl = false, shift = false, alt = false;
        var nonModifiers = new List<string>();

        foreach (var part in tokens)
        {
            if (part.Equals("VK_CONTROL", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("CTRL", StringComparison.OrdinalIgnoreCase))
            {
                ctrl = true;
            }
            else if (part.Equals("VK_SHIFT", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
            {
                shift = true;
            }
            else if (part.Equals("VK_MENU", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("ALT", StringComparison.OrdinalIgnoreCase))
            {
                alt = true;
            }
            else if (part.StartsWith("NO_", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("MODIFIERS", StringComparison.OrdinalIgnoreCase))
            {
                // Ignore modifier-exclusion tokens.
            }
            else
            {
                nonModifiers.Add(part);
            }
        }

        var primary = string.Join(" ", nonModifiers);
        if (IsNumFormat(primary) && (ctrl || shift || alt))
        {
            var digit = Regex.Match(primary, @"\d+").Value;
            primary = "VK_NUMPAD" + digit;
        }
        return (ctrl, shift, alt, primary);
    }

    /// <summary>
    /// <summary>Formats key lists into aggregated friendly display strings.</summary>
    /// </summary>
    /// <summary>
    /// Merges newly recorded key tokens with original key binding's negative modifier constraints.
    /// Handles conflict elimination while preserving non-conflicting negative modifiers.
    /// </summary>
    public static string MergeRecordedKeyWithOriginalConstraints(string? originalKey, string? newKey)
    {
        if (string.IsNullOrWhiteSpace(newKey))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(originalKey))
            return newKey.Trim();

        return KeyBindingExpression.Parse(originalKey)
            .WithRecordedKeys(newKey)
            .ToIniString();
    }

    public static string FormatKeysAggregateDisplay(IEnumerable<string>? keys, bool isChinese)
    {
        if (keys == null) return string.Empty;
        var list = NormalizeKeys(keys);
        if (list.Count == 0) return string.Empty;

        var friendlyList = list
            .Select(k => ConvertToFriendlyText(k, isChinese))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (friendlyList.Count == 0) return string.Empty;
        var separator = isChinese ? " 或 " : " or ";
        return string.Join(separator, friendlyList);
    }
}