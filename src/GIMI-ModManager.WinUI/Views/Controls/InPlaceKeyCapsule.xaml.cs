using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Helpers;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Text;
using Windows.Gaming.Input;
using Windows.System;
using Windows.UI;

namespace GIMI_ModManager.WinUI.Views.Controls;

public sealed partial class InPlaceKeyCapsule : UserControl
{
    public static readonly DependencyProperty RawKeyProperty = DependencyProperty.Register(
        nameof(RawKey), typeof(string), typeof(InPlaceKeyCapsule), new PropertyMetadata(string.Empty, OnRawKeyChangedStatic));

    public static readonly DependencyProperty IsUnsavedProperty = DependencyProperty.Register(
        nameof(IsUnsaved), typeof(bool), typeof(InPlaceKeyCapsule), new PropertyMetadata(false, OnIsUnsavedChangedStatic));

    public static readonly DependencyProperty IsForwardProperty = DependencyProperty.Register(
        nameof(IsForward), typeof(bool), typeof(InPlaceKeyCapsule), new PropertyMetadata(true));

    public static readonly DependencyProperty CanAddReverseKeyProperty = DependencyProperty.Register(
        nameof(CanAddReverseKey), typeof(bool), typeof(InPlaceKeyCapsule), new PropertyMetadata(false, OnCanAddReverseKeyChangedStatic));

    public static readonly DependencyProperty CanDeleteProperty = DependencyProperty.Register(
        nameof(CanDelete), typeof(bool), typeof(InPlaceKeyCapsule), new PropertyMetadata(false, OnCanDeleteChangedStatic));

    public static readonly DependencyProperty CanAddAlternativeProperty = DependencyProperty.Register(
        nameof(CanAddAlternative), typeof(bool), typeof(InPlaceKeyCapsule), new PropertyMetadata(true, OnCanAddAlternativeChangedStatic));

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(InPlaceKeyCapsule), new PropertyMetadata(string.Empty, OnPlaceholderTextChangedStatic));

    public static readonly DependencyProperty AutoStartRecordingProperty = DependencyProperty.Register(
        nameof(AutoStartRecording), typeof(bool), typeof(InPlaceKeyCapsule), new PropertyMetadata(false, OnAutoStartRecordingChangedStatic));

    public string RawKey
    {
        get => (string)GetValue(RawKeyProperty);
        set => SetValue(RawKeyProperty, value);
    }

    public bool IsUnsaved
    {
        get => (bool)GetValue(IsUnsavedProperty);
        set => SetValue(IsUnsavedProperty, value);
    }

    public bool IsForward
    {
        get => (bool)GetValue(IsForwardProperty);
        set => SetValue(IsForwardProperty, value);
    }

    public bool CanAddReverseKey
    {
        get => (bool)GetValue(CanAddReverseKeyProperty);
        set => SetValue(CanAddReverseKeyProperty, value);
    }

    public bool CanDelete
    {
        get => (bool)GetValue(CanDeleteProperty);
        set => SetValue(CanDeleteProperty, value);
    }

    public bool CanAddAlternative
    {
        get => (bool)GetValue(CanAddAlternativeProperty);
        set => SetValue(CanAddAlternativeProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public bool AutoStartRecording
    {
        get => (bool)GetValue(AutoStartRecordingProperty);
        set => SetValue(AutoStartRecordingProperty, value);
    }

    public event EventHandler<string>? KeyRecorded;
    public event EventHandler? RecordingCancelled;
    public event EventHandler? ReverseKeyRequested;
    public event EventHandler? AddAlternativeRequested;
    public event EventHandler? DeleteRequested;

    private static void OnAutoStartRecordingChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InPlaceKeyCapsule capsule && (bool)e.NewValue)
        {
            capsule.DispatcherQueue?.TryEnqueue(() =>
            {
                capsule.StartRecording();
                capsule.AutoStartRecording = false;
            });
        }
    }

    private static void OnCanAddReverseKeyChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InPlaceKeyCapsule capsule)
        {
            var canAdd = (bool)e.NewValue;
            capsule.AddReverseKeyMenuItem.Visibility = canAdd ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void OnCanDeleteChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InPlaceKeyCapsule capsule)
        {
            var canDelete = (bool)e.NewValue;
            capsule.DeleteKeyMenuItem.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
            capsule.ClearKeyMenuItem.Visibility = canDelete ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static void OnCanAddAlternativeChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InPlaceKeyCapsule capsule)
        {
            var canAdd = (bool)e.NewValue;
            capsule.AddAlternativeMenuItem.Visibility = canAdd ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private bool _isRecording;
    public bool IsRecording => _isRecording;
    private bool _isPointerOver;
    private bool _isPressed;
    private DispatcherTimer? _gamepadTimer;

    public InPlaceKeyCapsule()
    {
        InitializeComponent();
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += (_, _) => UpdateAppearance();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateAppearance();
        UpdateMenuState();
        if (AutoStartRecording)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                StartRecording();
                AutoStartRecording = false;
            });
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CancelRecording();
        StopGamepadPolling();
    }

    private static void OnRawKeyChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InPlaceKeyCapsule capsule && !capsule._isRecording)
        {
            capsule.UpdateAppearance();
        }
    }

    private static void OnIsUnsavedChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InPlaceKeyCapsule capsule)
        {
            capsule.UpdateAppearance();
        }
    }

    private static void OnPlaceholderTextChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is InPlaceKeyCapsule capsule && !capsule._isRecording && string.IsNullOrWhiteSpace(capsule.RawKey))
        {
            capsule.UpdateAppearance();
        }
    }

    private bool IsChinese =>
        (App.GetService<ILanguageLocalizer>()?.CurrentLanguage?.LanguageCode ?? System.Globalization.CultureInfo.CurrentUICulture.Name)
        .StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private void UpdateVisualState(bool useTransitions = true)
    {
        if (_isRecording)
        {
            VisualStateManager.GoToState(this, "RecordingState", useTransitions);
            return;
        }

        if (string.IsNullOrWhiteSpace(RawKey))
        {
            var state = _isPointerOver ? "EmptyHoverState" : "EmptyState";
            VisualStateManager.GoToState(this, state, useTransitions);
        }
        else
        {
            string state;
            if (_isPressed)
            {
                state = "StandardPressedState";
            }
            else if (_isPointerOver)
            {
                state = "StandardHoverState";
            }
            else
            {
                state = "StandardState";
            }
            VisualStateManager.GoToState(this, state, useTransitions);
        }
    }

    public void UpdateAppearance()
    {
        if (_isRecording)
        {
            UnsavedDot.Visibility = Visibility.Collapsed;
            UpdateVisualState();
            UpdateKeyboardPreviewText();
            ToolTipService.SetToolTip(CapsuleButton, IsChinese ? "按下键盘或手柄按键完成录制 (Esc 取消，Delete 清空)" : "Press keyboard or gamepad key (Esc to cancel, Delete to clear)");
            return;
        }

        UnsavedDot.Visibility = IsUnsaved ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(RawKey))
        {
            var defaultPlaceholder = IsChinese ? "未绑定" : "None";
            LabelTextBlock.Text = !string.IsNullOrWhiteSpace(PlaceholderText) ? PlaceholderText : defaultPlaceholder;
            ToolTipService.SetToolTip(CapsuleButton, IsChinese ? "点击录制按键 (右键打开菜单)" : "Click to record key (Right-click for options)");
        }
        else
        {
            var tokens = KeyMappingHelpers.SplitMultiKeys(RawKey);
            var friendly = KeyMappingHelpers.FormatKeysAggregateDisplay(tokens, IsChinese);
            LabelTextBlock.Text = friendly;

            var tip = !string.Equals(friendly, RawKey, StringComparison.OrdinalIgnoreCase)
                ? $"{friendly} ({RawKey})"
                : RawKey;
            if (IsUnsaved)
            {
                tip = (IsChinese ? "[未保存] " : "[Unsaved] ") + tip;
            }
            ToolTipService.SetToolTip(CapsuleButton, tip + (IsChinese ? "\n点击重新录制，右键更多选项" : "\nClick to re-record, right-click for options"));
        }

        UpdateVisualState();

        if (IsUnsaved && Application.Current.Resources.TryGetValue("SystemFillColorCautionBrush", out var cautionObj) && cautionObj is Brush cautionBrush)
        {
            CapsuleBorder.BorderBrush = cautionBrush;
        }
    }

    private void OnCapsuleClicked(object sender, RoutedEventArgs e)
    {
        if (!_isRecording)
        {
            StartRecording();
        }
    }

    private readonly HashSet<VirtualKey> _currentlyPressedKeys = [];
    private readonly List<string> _capturedKeyTokens = [];
    private bool _keyboardHasPressedThisSession;

    public void StartRecording()
    {
        _isRecording = true;
        _currentlyPressedKeys.Clear();
        _capturedKeyTokens.Clear();
        _keyboardHasPressedThisSession = false;
        UpdateAppearance();
        CapsuleButton.Focus(FocusState.Programmatic);
        StartGamepadPolling();
    }

    public void ApplyKeyDirectly(string newRawKey)
    {
        if (_isRecording)
        {
            _isRecording = false;
            _currentlyPressedKeys.Clear();
            _capturedKeyTokens.Clear();
            _keyboardHasPressedThisSession = false;
            StopGamepadPolling();
        }

        var finalKey = KeyMappingHelpers.MergeRecordedKeyWithOriginalConstraints(RawKey, newRawKey);
        RawKey = finalKey;
        IsUnsaved = true;
        UpdateAppearance();
        KeyRecorded?.Invoke(this, finalKey);
    }

    private void FinishRecording(string newRawKey)
    {
        ApplyKeyDirectly(newRawKey);
    }

    private void CancelRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;
        _currentlyPressedKeys.Clear();
        _capturedKeyTokens.Clear();
        _keyboardHasPressedThisSession = false;
        StopGamepadPolling();
        UpdateAppearance();
        RecordingCancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnCapsuleLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            CancelRecording();
        }
    }

    private void OnCapsulePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_isRecording)
        {
            var ptr = e.GetCurrentPoint(CapsuleButton);
            if (ptr.Properties.IsLeftButtonPressed)
            {
                _isPressed = true;
                UpdateVisualState();
            }
            return;
        }

        var props = e.GetCurrentPoint(CapsuleButton).Properties;
        string? mouseKey = null;

        if (props.IsXButton1Pressed) mouseKey = "VK_XBUTTON1";
        else if (props.IsXButton2Pressed) mouseKey = "VK_XBUTTON2";
        else if (props.IsMiddleButtonPressed) mouseKey = "VK_MBUTTON";

        if (mouseKey != null)
        {
            e.Handled = true;
            if (_capturedKeyTokens.Count > 0)
            {
                if (!_capturedKeyTokens.Contains(mouseKey, StringComparer.OrdinalIgnoreCase))
                {
                    _capturedKeyTokens.Add(mouseKey);
                }
                FinishRecording(string.Join(" ", _capturedKeyTokens));
            }
            else
            {
                FinishRecording(mouseKey);
            }
        }
    }

    private void OnCapsulePointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!_isRecording) return;

        e.Handled = true;
        var delta = e.GetCurrentPoint(CapsuleButton).Properties.MouseWheelDelta;
        var wheelKey = delta > 0 ? "WheelUp" : "WheelDown";

        if (_capturedKeyTokens.Count > 0)
        {
            if (!_capturedKeyTokens.Contains(wheelKey, StringComparer.OrdinalIgnoreCase))
            {
                _capturedKeyTokens.Add(wheelKey);
            }
            FinishRecording(string.Join(" ", _capturedKeyTokens));
        }
        else
        {
            FinishRecording(wheelKey);
        }
    }

    private void OnCapsulePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_isRecording) return;

        // Pressing Escape cancels recording
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CancelRecording();
            return;
        }

        // Pressing Enter confirms the currently captured keys if any
        if (e.Key == VirtualKey.Enter && _capturedKeyTokens.Count > 0)
        {
            e.Handled = true;
            FinishRecording(string.Join(" ", _capturedKeyTokens));
            return;
        }

        // Pressing Delete or Backspace clears the key
        // Note: Numpad '.' with Shift held produces VK_DELETE with ScanCode 0x53 and !IsExtendedKey.
        // It represents a physical numpad key, NOT a clear action.
        var isNumpadPeriodAsDelete = e.Key == VirtualKey.Delete && !e.KeyStatus.IsExtendedKey && e.KeyStatus.ScanCode == 0x53;
        if ((e.Key is VirtualKey.Delete or VirtualKey.Back) && !isNumpadPeriodAsDelete)
        {
            e.Handled = true;
            FinishRecording(string.Empty);
            return;
        }

        e.Handled = true;

        if (_currentlyPressedKeys.Contains(e.Key))
        {
            return;
        }

        _currentlyPressedKeys.Add(e.Key);
        _keyboardHasPressedThisSession = true;

        var token = KeyMappingHelpers.ConvertVirtualKeyToGimiInputKey(
            (int)e.Key,
            e.KeyStatus.ScanCode,
            e.KeyStatus.IsExtendedKey);

        if (!_capturedKeyTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
        {
            if (IsModifierToken(token))
            {
                var firstNonModifierIndex = _capturedKeyTokens.FindIndex(t => !IsModifierToken(t));
                if (firstNonModifierIndex >= 0)
                {
                    _capturedKeyTokens.Insert(firstNonModifierIndex, token);
                }
                else
                {
                    _capturedKeyTokens.Add(token);
                }
            }
            else
            {
                _capturedKeyTokens.Add(token);
            }
        }

        SyncPhysicalModifierStates();
        UpdateKeyboardPreviewText();
    }

    private void OnCapsulePreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (!_isRecording) return;

        e.Handled = true;
        _currentlyPressedKeys.Remove(e.Key);

        if (e.Key is VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.Control)
            _currentlyPressedKeys.Remove(VirtualKey.Control);
        if (e.Key is VirtualKey.LeftShift or VirtualKey.RightShift or VirtualKey.Shift)
            _currentlyPressedKeys.Remove(VirtualKey.Shift);
        if (e.Key is VirtualKey.LeftMenu or VirtualKey.RightMenu or VirtualKey.Menu)
            _currentlyPressedKeys.Remove(VirtualKey.Menu);

        if (IsKeyPhysicallyDown(VirtualKey.Control)) _currentlyPressedKeys.Add(VirtualKey.Control);
        else _currentlyPressedKeys.Remove(VirtualKey.Control);

        if (IsKeyPhysicallyDown(VirtualKey.Shift)) _currentlyPressedKeys.Add(VirtualKey.Shift);
        else _currentlyPressedKeys.Remove(VirtualKey.Shift);

        if (IsKeyPhysicallyDown(VirtualKey.Menu)) _currentlyPressedKeys.Add(VirtualKey.Menu);
        else _currentlyPressedKeys.Remove(VirtualKey.Menu);

        // Auto-commit requires:
        // 1. At least one key was physically pressed in this session
        // 2. All physical keys have been released
        // 3. At least one NON-modifier key has been captured (never commit incomplete modifier-only combos automatically)
        var hasNonModifierCaptured = _capturedKeyTokens.Any(t => !IsModifierToken(t));

        if (_keyboardHasPressedThisSession && _currentlyPressedKeys.Count == 0 && hasNonModifierCaptured)
        {
            var finalRawKey = string.Join(" ", _capturedKeyTokens);
            FinishRecording(finalRawKey);
        }
        else
        {
            UpdateKeyboardPreviewText();
        }
    }

    private void SyncPhysicalModifierStates()
    {
        var ctrl = IsKeyPhysicallyDown(VirtualKey.Control);
        var shift = IsKeyPhysicallyDown(VirtualKey.Shift);
        var alt = IsKeyPhysicallyDown(VirtualKey.Menu);

        if (ctrl && !_capturedKeyTokens.Contains("VK_CONTROL", StringComparer.OrdinalIgnoreCase))
        {
            _capturedKeyTokens.Insert(0, "VK_CONTROL");
        }
        if (shift && !_capturedKeyTokens.Contains("VK_SHIFT", StringComparer.OrdinalIgnoreCase))
        {
            var idx = _capturedKeyTokens.IndexOf("VK_CONTROL");
            _capturedKeyTokens.Insert(idx >= 0 ? idx + 1 : 0, "VK_SHIFT");
        }
        if (alt && !_capturedKeyTokens.Contains("VK_MENU", StringComparer.OrdinalIgnoreCase))
        {
            var nonModIdx = _capturedKeyTokens.FindIndex(t => !IsModifierToken(t));
            if (nonModIdx >= 0) _capturedKeyTokens.Insert(nonModIdx, "VK_MENU");
            else _capturedKeyTokens.Add("VK_MENU");
        }
    }

    private static bool IsKeyPhysicallyDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private static bool IsModifierToken(string token) =>
        token is "VK_CONTROL" or "VK_SHIFT" or "VK_MENU" or "CTRL" or "SHIFT" or "ALT";

    private void UpdateKeyboardPreviewText()
    {
        if (_capturedKeyTokens.Count == 0)
        {
            LabelTextBlock.Text = IsChinese ? "请按键..." : "Press a key...";
            return;
        }

        var comboString = string.Join(" ", _capturedKeyTokens);
        var preview = KeyMappingHelpers.ConvertToFriendlyText(comboString, IsChinese);

        var onlyModifiers = _capturedKeyTokens.All(IsModifierToken);
        if (onlyModifiers)
        {
            preview += " + ...";
        }

        LabelTextBlock.Text = preview;
    }

    private readonly List<string> _capturedGamepadButtons = [];
    private bool _gamepadHasPressedThisSession;

    private void StartGamepadPolling()
    {
        _capturedGamepadButtons.Clear();
        _gamepadHasPressedThisSession = false;
        _gamepadTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _gamepadTimer.Tick -= GamepadTimer_Tick;
        _gamepadTimer.Tick += GamepadTimer_Tick;
        _gamepadTimer.Start();
    }

    private void StopGamepadPolling()
    {
        if (_gamepadTimer != null)
        {
            _gamepadTimer.Stop();
            _gamepadTimer.Tick -= GamepadTimer_Tick;
        }
        _capturedGamepadButtons.Clear();
        _gamepadHasPressedThisSession = false;
    }

    private void GamepadTimer_Tick(object? sender, object e)
    {
        if (!_isRecording)
        {
            StopGamepadPolling();
            return;
        }

        var gamepads = Gamepad.Gamepads;
        if (gamepads.Count == 0) return;

        var currentPressed = new List<string>();
        foreach (var pad in gamepads)
        {
            var reading = pad.GetCurrentReading();

            if (reading.Buttons.HasFlag(GamepadButtons.LeftShoulder)) currentPressed.Add("XB_LEFT_SHOULDER");
            if (reading.Buttons.HasFlag(GamepadButtons.RightShoulder)) currentPressed.Add("XB_RIGHT_SHOULDER");
            if (reading.LeftTrigger > 0.5) currentPressed.Add("XB_LEFT_TRIGGER");
            if (reading.RightTrigger > 0.5) currentPressed.Add("XB_RIGHT_TRIGGER");

            if (reading.Buttons.HasFlag(GamepadButtons.A)) currentPressed.Add("XB_A");
            if (reading.Buttons.HasFlag(GamepadButtons.B)) currentPressed.Add("XB_B");
            if (reading.Buttons.HasFlag(GamepadButtons.X)) currentPressed.Add("XB_X");
            if (reading.Buttons.HasFlag(GamepadButtons.Y)) currentPressed.Add("XB_Y");

            if (reading.Buttons.HasFlag(GamepadButtons.DPadUp)) currentPressed.Add("XB_DPAD_UP");
            if (reading.Buttons.HasFlag(GamepadButtons.DPadDown)) currentPressed.Add("XB_DPAD_DOWN");
            if (reading.Buttons.HasFlag(GamepadButtons.DPadLeft)) currentPressed.Add("XB_DPAD_LEFT");
            if (reading.Buttons.HasFlag(GamepadButtons.DPadRight)) currentPressed.Add("XB_DPAD_RIGHT");

            if (reading.Buttons.HasFlag(GamepadButtons.View)) currentPressed.Add("XB_BACK");
            if (reading.Buttons.HasFlag(GamepadButtons.Menu)) currentPressed.Add("XB_START");
            if (reading.Buttons.HasFlag(GamepadButtons.LeftThumbstick)) currentPressed.Add("XB_LEFT_THUMB");
            if (reading.Buttons.HasFlag(GamepadButtons.RightThumbstick)) currentPressed.Add("XB_RIGHT_THUMB");
        }

        if (currentPressed.Count > 0)
        {
            _gamepadHasPressedThisSession = true;
            foreach (var btn in currentPressed)
            {
                if (!_capturedGamepadButtons.Contains(btn))
                {
                    _capturedGamepadButtons.Add(btn);
                }
            }

            var comboString = string.Join(" ", _capturedGamepadButtons);
            var previewText = KeyMappingHelpers.ConvertToFriendlyText(comboString, IsChinese);
            LabelTextBlock.Text = previewText;
        }
        else if (_gamepadHasPressedThisSession && _capturedGamepadButtons.Count > 0)
        {
            var finalKey = string.Join(" ", _capturedGamepadButtons);
            ApplyKeyDirectly(finalKey);
        }
    }

    private void OnCapsulePointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = true;
        if (!_isRecording)
        {
            UpdateVisualState();
        }
    }

    private void OnCapsulePointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOver = false;
        _isPressed = false;
        if (!_isRecording)
        {
            UpdateVisualState();
        }
    }

    private void OnCapsulePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isPressed = false;
        if (!_isRecording)
        {
            UpdateVisualState();
        }
    }

    private void ContextMenu_Opening(object? sender, object e)
    {
        UpdateMenuState();
    }

    public void UpdateMenuState()
    {
        UpdateMenuLocalization();
        AddReverseKeyMenuItem.Visibility = CanAddReverseKey ? Visibility.Visible : Visibility.Collapsed;
        DeleteKeyMenuItem.Visibility = CanDelete ? Visibility.Visible : Visibility.Collapsed;
        ClearKeyMenuItem.Visibility = CanDelete ? Visibility.Collapsed : Visibility.Visible;
        AddAlternativeMenuItem.Visibility = CanAddAlternative ? Visibility.Visible : Visibility.Collapsed;
        ReverseKeySeparator.Visibility = CanAddReverseKey ? Visibility.Visible : Visibility.Collapsed;

        var hasKey = !string.IsNullOrWhiteSpace(RawKey);
        NegativeModifiersSubMenu.Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed;
        ExclusionSeparator.Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed;

        if (hasKey)
        {
            var expr = KeyBindingExpression.Parse(RawKey);
            NoModifiersToggleItem.IsChecked = expr.NegativeModifiers.HasFlag(NegativeModifierMask.NoModifiers);
            NoAltToggleItem.IsChecked = expr.NegativeModifiers.HasFlag(NegativeModifierMask.NoAlt);
            NoCtrlToggleItem.IsChecked = expr.NegativeModifiers.HasFlag(NegativeModifierMask.NoCtrl);
            NoShiftToggleItem.IsChecked = expr.NegativeModifiers.HasFlag(NegativeModifierMask.NoShift);

            var hasAlt = expr.Modifiers.HasFlag(ModifierMask.Alt);
            var hasCtrl = expr.Modifiers.HasFlag(ModifierMask.Ctrl);
            var hasShift = expr.Modifiers.HasFlag(ModifierMask.Shift);
            var hasAnyMod = hasAlt || hasCtrl || hasShift;

            NoModifiersToggleItem.IsEnabled = !hasAnyMod;
            NoAltToggleItem.IsEnabled = !hasAlt && !NoModifiersToggleItem.IsChecked;
            NoCtrlToggleItem.IsEnabled = !hasCtrl && !NoModifiersToggleItem.IsChecked;
            NoShiftToggleItem.IsEnabled = !hasShift && !NoModifiersToggleItem.IsChecked;
        }
    }

    private void UpdateMenuLocalization()
    {
        var zh = IsChinese;
        AddAlternativeMenuItem.Text = zh ? "添加备选按键" : "Add Alternative Key";
        AddReverseKeyMenuItem.Text = zh ? "添加反向按键" : "Add Reverse Key";
        DeleteKeyMenuItem.Text = zh ? "删除此按键" : "Delete Key";
        ClearKeyMenuItem.Text = zh ? "清空按键" : "Clear Key";

        NegativeModifiersSubMenu.Text = zh ? "互斥规则 (排除)" : "Exclusion Rules";
        NoModifiersToggleItem.Text = zh ? "仅单键生效 (no_modifiers)" : "Pure Single Key (no_modifiers)";
        NoAltToggleItem.Text = zh ? "排除 Alt 键 (no_alt)" : "Exclude Alt (no_alt)";
        NoCtrlToggleItem.Text = zh ? "排除 Ctrl 键 (no_ctrl)" : "Exclude Ctrl (no_ctrl)";
        NoShiftToggleItem.Text = zh ? "排除 Shift 键 (no_shift)" : "Exclude Shift (no_shift)";
    }

    private void OnToggleNegativeModifier(NegativeModifierMask flag)
    {
        if (string.IsNullOrWhiteSpace(RawKey)) return;
        var expr = KeyBindingExpression.Parse(RawKey);
        var updated = expr.ToggleNegativeModifier(flag);
        var newRaw = updated.ToIniString();

        RawKey = newRaw;
        IsUnsaved = true;
        UpdateAppearance();
        KeyRecorded?.Invoke(this, newRaw);
    }

    private void OnToggleNoModifiersClicked(object sender, RoutedEventArgs e) =>
        OnToggleNegativeModifier(NegativeModifierMask.NoModifiers);

    private void OnToggleNoAltClicked(object sender, RoutedEventArgs e) =>
        OnToggleNegativeModifier(NegativeModifierMask.NoAlt);

    private void OnToggleNoCtrlClicked(object sender, RoutedEventArgs e) =>
        OnToggleNegativeModifier(NegativeModifierMask.NoCtrl);

    private void OnToggleNoShiftClicked(object sender, RoutedEventArgs e) =>
        OnToggleNegativeModifier(NegativeModifierMask.NoShift);

    private void OnAddAlternativeClicked(object sender, RoutedEventArgs e)
    {
        AddAlternativeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnAddReverseKeyClicked(object sender, RoutedEventArgs e)
    {
        ReverseKeyRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        ApplyKeyDirectly(string.Empty);
    }
}