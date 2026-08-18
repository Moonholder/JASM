namespace GIMI_ModManager.WinUI.Models.Settings;

public class AppBehaviorSettings
{
    public const string Key = "AppBehaviorSettings";

    public bool RestoreFocusAfterSync { get; set; } = true;
    public uint HotkeyModifiers { get; set; } = 0x0001; // MOD_ALT by default
    public uint HotkeyVirtualKey { get; set; } = 192;   // OEM_3 (backtick) by default
    public bool IsHotkeyConfigured { get; set; } = false;
}