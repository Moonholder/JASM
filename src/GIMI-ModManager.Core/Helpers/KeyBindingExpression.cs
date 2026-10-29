namespace GIMI_ModManager.Core.Helpers;

[Flags]
public enum ModifierMask
{
    None = 0,
    Ctrl = 1 << 0,
    Shift = 1 << 1,
    Alt = 1 << 2
}

[Flags]
public enum NegativeModifierMask
{
    None = 0,
    NoModifiers = 1 << 0,
    NoCtrl = 1 << 1,
    NoShift = 1 << 2,
    NoAlt = 1 << 3
}

public sealed record KeyBindingExpression
{
    public IReadOnlyList<string> TriggerKeys { get; init; } = [];
    public ModifierMask Modifiers { get; init; } = ModifierMask.None;
    public NegativeModifierMask NegativeModifiers { get; init; } = NegativeModifierMask.None;

    public bool HasAnyPositiveModifier => Modifiers != ModifierMask.None;
    public bool HasAnyNegativeModifier => NegativeModifiers != NegativeModifierMask.None;

    public static KeyBindingExpression Empty { get; } = new();

    public static KeyBindingExpression Parse(string? rawExpression)
    {
        if (string.IsNullOrWhiteSpace(rawExpression))
            return Empty;

        var tokens = KeyMappingHelpers.TokenRegexGenerated().Matches(rawExpression)
            .Select(m => m.Value.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        if (tokens.Count == 0)
            return Empty;

        var triggers = new List<string>();
        var modifiers = ModifierMask.None;
        var negativeModifiers = NegativeModifierMask.None;

        foreach (var token in tokens)
        {
            if (token.Equals("NO_MODIFIERS", StringComparison.OrdinalIgnoreCase))
            {
                negativeModifiers |= NegativeModifierMask.NoModifiers;
            }
            else if (token.Equals("NO_ALT", StringComparison.OrdinalIgnoreCase))
            {
                negativeModifiers |= NegativeModifierMask.NoAlt;
            }
            else if (token.Equals("NO_CTRL", StringComparison.OrdinalIgnoreCase))
            {
                negativeModifiers |= NegativeModifierMask.NoCtrl;
            }
            else if (token.Equals("NO_SHIFT", StringComparison.OrdinalIgnoreCase))
            {
                negativeModifiers |= NegativeModifierMask.NoShift;
            }
            else if (IsCtrlToken(token))
            {
                modifiers |= ModifierMask.Ctrl;
            }
            else if (IsShiftToken(token))
            {
                modifiers |= ModifierMask.Shift;
            }
            else if (IsAltToken(token))
            {
                modifiers |= ModifierMask.Alt;
            }
            else if (!token.Equals("MODIFIERS", StringComparison.OrdinalIgnoreCase))
            {
                triggers.Add(token);
            }
        }

        negativeModifiers = SanitizeNegativeModifiers(modifiers, negativeModifiers);

        return new KeyBindingExpression
        {
            TriggerKeys = triggers,
            Modifiers = modifiers,
            NegativeModifiers = negativeModifiers
        };
    }

    public KeyBindingExpression WithRecordedKeys(string? newKeySequence)
    {
        if (string.IsNullOrWhiteSpace(newKeySequence))
            return Empty;

        var parsedNew = Parse(newKeySequence);
        var preservedNegative = SanitizeNegativeModifiers(parsedNew.Modifiers, NegativeModifiers);

        return parsedNew with
        {
            NegativeModifiers = preservedNegative
        };
    }

    public KeyBindingExpression SetNegativeModifier(NegativeModifierMask flag, bool enabled)
    {
        var updated = enabled
            ? (NegativeModifiers | flag)
            : (NegativeModifiers & ~flag);

        if (enabled && flag == NegativeModifierMask.NoModifiers)
        {
            updated = NegativeModifierMask.NoModifiers;
        }
        else if (enabled && flag != NegativeModifierMask.NoModifiers)
        {
            updated &= ~NegativeModifierMask.NoModifiers;
        }

        return this with
        {
            NegativeModifiers = SanitizeNegativeModifiers(Modifiers, updated)
        };
    }

    public KeyBindingExpression ToggleNegativeModifier(NegativeModifierMask flag)
    {
        var isCurrentlyEnabled = NegativeModifiers.HasFlag(flag);
        return SetNegativeModifier(flag, !isCurrentlyEnabled);
    }

    public string ToIniString()
    {
        var tokens = new List<string>();

        if (NegativeModifiers.HasFlag(NegativeModifierMask.NoModifiers))
        {
            tokens.Add("no_modifiers");
        }

        if (Modifiers.HasFlag(ModifierMask.Ctrl)) tokens.Add("VK_CONTROL");
        if (Modifiers.HasFlag(ModifierMask.Shift)) tokens.Add("VK_SHIFT");
        if (Modifiers.HasFlag(ModifierMask.Alt)) tokens.Add("VK_MENU");

        tokens.AddRange(TriggerKeys);

        if (NegativeModifiers.HasFlag(NegativeModifierMask.NoAlt)) tokens.Add("no_alt");
        if (NegativeModifiers.HasFlag(NegativeModifierMask.NoCtrl)) tokens.Add("no_ctrl");
        if (NegativeModifiers.HasFlag(NegativeModifierMask.NoShift)) tokens.Add("no_shift");

        return string.Join(" ", tokens);
    }

    private static NegativeModifierMask SanitizeNegativeModifiers(ModifierMask positive, NegativeModifierMask negative)
    {
        if (positive == ModifierMask.None)
            return negative;

        var sanitized = negative & ~NegativeModifierMask.NoModifiers;

        if (positive.HasFlag(ModifierMask.Alt))
            sanitized &= ~NegativeModifierMask.NoAlt;

        if (positive.HasFlag(ModifierMask.Ctrl))
            sanitized &= ~NegativeModifierMask.NoCtrl;

        if (positive.HasFlag(ModifierMask.Shift))
            sanitized &= ~NegativeModifierMask.NoShift;

        return sanitized;
    }

    public static bool IsCtrlToken(string token) =>
        token.Equals("CTRL", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("VK_CONTROL", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("VK_LCONTROL", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("VK_RCONTROL", StringComparison.OrdinalIgnoreCase);

    public static bool IsShiftToken(string token) =>
        token.Equals("SHIFT", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("VK_SHIFT", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("VK_LSHIFT", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("VK_RSHIFT", StringComparison.OrdinalIgnoreCase);

    public static bool IsAltToken(string token) =>
        token.Equals("ALT", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("VK_MENU", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("VK_LMENU", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("VK_RMENU", StringComparison.OrdinalIgnoreCase);
}