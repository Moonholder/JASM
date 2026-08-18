using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.Core.Contracts.Services;
using Serilog;

namespace GIMI_ModManager.WinUI.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly NotificationManager _notificationManager;
    private readonly ILanguageLocalizer _localizer;
    private readonly ILogger _logger;

    private IntPtr _hWnd = IntPtr.Zero;
    private bool _isHooked;
    private uint _currentModifiers;
    private uint _currentVirtualKey;

    private const int HOTKEY_ID = 422026;
    private const int SUBCLASS_ID = 202606;

    private SUBCLASSPROC? _subclassProc;

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [StructLayout(LayoutKind.Sequential)]
    private struct CHANGEFILTERSTRUCT
    {
        public uint cbSize;
        public uint ExtStatus;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, ref CHANGEFILTERSTRUCT pChangeFilterStruct);

    [DllImport("comctl32.dll", CharSet = CharSet.Auto)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", CharSet = CharSet.Auto)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass);

    [DllImport("comctl32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    // Modifiers flags
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint MSGFLT_ALLOW = 1;
    private const uint WM_HOTKEY = 0x0312;

    public HotkeyService(
        ILocalSettingsService localSettingsService,
        NotificationManager notificationManager,
        ILanguageLocalizer localizer,
        ILogger logger)
    {
        _localSettingsService = localSettingsService;
        _notificationManager = notificationManager;
        _localizer = localizer;
        _logger = logger.ForContext<HotkeyService>();
    }

    public async Task InitializeAsync(IntPtr hWnd)
    {
        _hWnd = hWnd;
        if (_hWnd == IntPtr.Zero)
        {
            _logger.Warning("Cannot initialize HotkeyService with null HWND");
            return;
        }

        var dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher == null)
        {
            _logger.Warning("DispatcherQueue is null, initializing on current thread");
            await InitializeInternalAsync();
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            await InitializeInternalAsync();
        }
        else
        {
            var tcs = new TaskCompletionSource();
            dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await InitializeInternalAsync();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            await tcs.Task;
        }
    }

    private async Task InitializeInternalAsync()
    {
        // Keep delegate reference alive to prevent GC collection
        _subclassProc = WndProc;
        _isHooked = SetWindowSubclass(_hWnd, _subclassProc, (IntPtr)SUBCLASS_ID, IntPtr.Zero);
        _logger.Debug("SetWindowSubclass status: {Status}", _isHooked);

        if (_isHooked)
        {
            var filterStruct = new CHANGEFILTERSTRUCT { cbSize = (uint)Marshal.SizeOf<CHANGEFILTERSTRUCT>() };
            bool filterSuccess = ChangeWindowMessageFilterEx(_hWnd, WM_HOTKEY, MSGFLT_ALLOW, ref filterStruct);
            _logger.Debug("ChangeWindowMessageFilterEx status: {Status}", filterSuccess);
        }

        // Load settings and register hotkey
        var settings = await _localSettingsService.ReadOrCreateSettingAsync<AppBehaviorSettings>(AppBehaviorSettings.Key, SettingScope.App);

        // Migrate to Alt + ` if never configured
        if (!settings.IsHotkeyConfigured)
        {
            settings.HotkeyModifiers = 0x0001; // MOD_ALT
            settings.HotkeyVirtualKey = 192;   // OEM_3 (backtick)
            settings.IsHotkeyConfigured = true;
            await _localSettingsService.SaveSettingAsync(AppBehaviorSettings.Key, settings, SettingScope.App);
        }

        _currentModifiers = settings.HotkeyModifiers;
        _currentVirtualKey = settings.HotkeyVirtualKey;

        if (_currentVirtualKey != 0)
        {
            RegisterCurrentHotkey(showNotifications: false);
        }
    }

    private void RegisterCurrentHotkey(bool showNotifications)
    {
        if (_hWnd == IntPtr.Zero || _currentVirtualKey == 0) return;

        // Unregister existing first
        UnregisterHotKey(_hWnd, HOTKEY_ID);

        // Register new with MOD_NOREPEAT to prevent repeating triggers when key is held down
        uint fsModifiers = _currentModifiers | MOD_NOREPEAT;

        bool success = RegisterHotKey(_hWnd, HOTKEY_ID, fsModifiers, _currentVirtualKey);
        if (success)
        {
            _logger.Information("Successfully registered global hotkey: Modifiers={Modifiers}, Key={Key}", _currentModifiers, _currentVirtualKey);
        }
        else
        {
            _logger.Error("Failed to register global hotkey. Modifiers={Modifiers}, Key={Key}, ErrorCode={ErrorCode}", _currentModifiers, _currentVirtualKey, Marshal.GetLastWin32Error());
            if (showNotifications)
            {
                _notificationManager.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("/Settings/HotkeyRegistrationFailedTitle", "Hotkey Registration Failed"),
                    _localizer.GetLocalizedStringOrDefault("/Settings/HotkeyRegistrationFailedMessage", "The selected shortcut is already in use by another application. Please choose another."),
                    TimeSpan.FromSeconds(5));
            }
        }
    }

    private void UnregisterCurrentHotkey()
    {
        if (_hWnd != IntPtr.Zero && _currentVirtualKey != 0)
        {
            UnregisterHotKey(_hWnd, HOTKEY_ID);
            _logger.Debug("Unregistered global hotkey");
        }
    }

    public async Task UpdateHotkeyAsync(uint modifiers, uint virtualKey)
    {
        var dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher == null)
        {
            _logger.Warning("DispatcherQueue is null, updating hotkey on current thread");
            await UpdateHotkeyInternalAsync(modifiers, virtualKey);
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            await UpdateHotkeyInternalAsync(modifiers, virtualKey);
        }
        else
        {
            var tcs = new TaskCompletionSource();
            dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await UpdateHotkeyInternalAsync(modifiers, virtualKey);
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            await tcs.Task;
        }
    }

    private async Task UpdateHotkeyInternalAsync(uint modifiers, uint virtualKey)
    {
        UnregisterCurrentHotkey();

        _currentModifiers = modifiers;
        _currentVirtualKey = virtualKey;

        // Save settings
        var settings = await _localSettingsService.ReadOrCreateSettingAsync<AppBehaviorSettings>(AppBehaviorSettings.Key, SettingScope.App);
        settings.HotkeyModifiers = modifiers;
        settings.HotkeyVirtualKey = virtualKey;
        await _localSettingsService.SaveSettingAsync(AppBehaviorSettings.Key, settings, SettingScope.App);

        if (_currentVirtualKey != 0)
        {
            RegisterCurrentHotkey(showNotifications: true);
        }
    }

    public void ToggleVisibility()
    {
        if (_hWnd != IntPtr.Zero)
        {
            IntPtr fgWindow = GetForegroundWindow();
            if (fgWindow == _hWnd)
            {
                // JASM is in the foreground, minimize it
                ShowWindow(_hWnd, SW_MINIMIZE);
            }
            else
            {
                // JASM is not in foreground, bring it to front
                ShowWindow(_hWnd, SW_RESTORE);
                SetForegroundWindow(_hWnd);

                // Also call WinUIEx BringToFront on the UI thread
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        App.MainWindow.BringToFront();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to bring MainWindow to front via WinUIEx");
                    }
                });
            }
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        const uint WM_HOTKEY_MSG = 0x0312;
        if (uMsg == WM_HOTKEY_MSG && wParam.ToInt32() == HOTKEY_ID)
        {
            // Toggle main window visibility
            ToggleVisibility();
            return IntPtr.Zero;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public void Dispose()
    {
        var dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher == null || dispatcher.HasThreadAccess)
        {
            DisposeInternal();
        }
        else
        {
            dispatcher.TryEnqueue(DisposeInternal);
        }
    }

    private void DisposeInternal()
    {
        UnregisterCurrentHotkey();

        if (_isHooked && _hWnd != IntPtr.Zero && _subclassProc != null)
        {
            RemoveWindowSubclass(_hWnd, _subclassProc, (IntPtr)SUBCLASS_ID);
            _isHooked = false;
            _logger.Debug("Removed window subclass hook");
        }
    }
}