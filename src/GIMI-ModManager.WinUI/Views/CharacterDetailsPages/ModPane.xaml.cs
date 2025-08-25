using GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GIMI_ModManager.WinUI.Views.CharacterDetailsPages;

/// <summary>
/// 虚拟键代码到友好显示文本的转换器
/// </summary>
public class VirtualKeyToFriendlyTextConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    private static readonly Dictionary<string, string> VirtualKeyMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // 方向键
        { "VK_UP", "↑" },
        { "VK_DOWN", "↓" },
        { "VK_LEFT", "←" },
        { "VK_RIGHT", "→" },

        // 功能键
        { "VK_F1", "F1" },
        { "VK_F2", "F2" },
        { "VK_F3", "F3" },
        { "VK_F4", "F4" },
        { "VK_F5", "F5" },
        { "VK_F6", "F6" },
        { "VK_F7", "F7" },
        { "VK_F8", "F8" },
        { "VK_F9", "F9" },
        { "VK_F10", "F10" },
        { "VK_F11", "F11" },
        { "VK_F12", "F12" },

        // 数字键
        { "VK_0", "0" },
        { "VK_1", "1" },
        { "VK_2", "2" },
        { "VK_3", "3" },
        { "VK_4", "4" },
        { "VK_5", "5" },
        { "VK_6", "6" },
        { "VK_7", "7" },
        { "VK_8", "8" },
        { "VK_9", "9" },

        // 字母键
        { "VK_A", "A" },
        { "VK_B", "B" },
        { "VK_C", "C" },
        { "VK_D", "D" },
        { "VK_E", "E" },
        { "VK_F", "F" },
        { "VK_G", "G" },
        { "VK_H", "H" },
        { "VK_I", "I" },
        { "VK_J", "J" },
        { "VK_K", "K" },
        { "VK_L", "L" },
        { "VK_M", "M" },
        { "VK_N", "N" },
        { "VK_O", "O" },
        { "VK_P", "P" },
        { "VK_Q", "Q" },
        { "VK_R", "R" },
        { "VK_S", "S" },
        { "VK_T", "T" },
        { "VK_U", "U" },
        { "VK_V", "V" },
        { "VK_W", "W" },
        { "VK_X", "X" },
        { "VK_Y", "Y" },
        { "VK_Z", "Z" },

        // 小键盘
        { "VK_NUMPAD0", "小键盘 0" },
        { "VK_NUMPAD1", "小键盘 1" },
        { "VK_NUMPAD2", "小键盘 2" },
        { "VK_NUMPAD3", "小键盘 3" },
        { "VK_NUMPAD4", "小键盘 4" },
        { "VK_NUMPAD5", "小键盘 5" },
        { "VK_NUMPAD6", "小键盘 6" },
        { "VK_NUMPAD7", "小键盘 7" },
        { "VK_NUMPAD8", "小键盘 8" },
        { "VK_NUMPAD9", "小键盘 9" },
        { "VK_MULTIPLY", "小键盘 *" },
        { "VK_ADD", "小键盘 +" },
        { "VK_SUBTRACT", "小键盘 -" },
        { "VK_DECIMAL", "小键盘 ." },
        { "VK_DIVIDE", "小键盘 /" },

        // 特殊键
        { "VK_ESCAPE", "ESC" },
        { "VK_TAB", "Tab" },
        { "VK_CAPITAL", "Caps Lock" },
        { "VK_SHIFT", "Shift" },
        { "VK_CONTROL", "Ctrl" },
        { "VK_MENU", "Alt" },
        { "VK_SPACE", "空格" },
        { "VK_RETURN", "回车" },
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
        { "VK_LBUTTON", "鼠标左键" },
        { "VK_RBUTTON", "鼠标右键" },
        { "VK_MBUTTON", "鼠标中键" },
        { "VK_XBUTTON1", "X1 鼠标侧键" },
        { "VK_XBUTTON2", "X2 鼠标侧键" },
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
        { "VK_OEM_COMMA", "逗号" },
        { ",", "逗号" },
        { "，", "逗号" },


        // 手柄按键
        { "XB_A", "手柄 A" },
        { "XB_B", "手柄 B" },
        { "XB_X", "手柄 X" },
        { "XB_Y", "手柄 Y" },
        { "XB_RIGHT_SHOULDER", "手柄 RB" },
        { "XB_LEFT_SHOULDER", "手柄 LB" },
        { "XB_RIGHT_TRIGGER", "手柄 RT" },
        { "XB_LEFT_TRIGGER", "手柄 LT" },
        { "XB_DPAD_UP", "手柄 ↑" },
        { "XB_DPAD_DOWN", "手柄 ↓" },
        { "XB_DPAD_LEFT", "手柄 ←" },
        { "XB_DPAD_RIGHT", "手柄 →" },
        { "XB_START", "手柄 Start" },
        { "XB_BACK", "手柄 Back" },
        { "XB_GUIDE", "手柄 Guide" },
        { "XB_LEFT_THUMB", "手柄 左摇杆" },
        { "XB_RIGHT_THUMB", "手柄 右摇杆" },
        { "XB_LEFT_THUMB_PRESS", "L3" },
        { "XB_RIGHT_THUMB_PRESS", "R3" },
        { "XB_LEFT_STICK_PRESS", "L3" },
        { "XB_RIGHT_STICK_PRESS", "R3" },

        // 修饰符前缀
        { "MODIFIERS", "修饰键" },
        { "ALT", "Alt" },
        { "SHIFT", "Shift" },
        { "CTRL", "Ctrl" },
        { "RCTRL", "右Ctrl" },
        { "VK_LSHIFT", "左 Shift" },
        { "VK_RSHIFT", "右 Shift" },
        { "VK_LCONTROL", "左 Ctrl" },
        { "VK_RCONTROL", "右 Ctrl" },
        { "VK_LMENU", "左 Alt" },
        { "VK_RMENU", "右 Alt" },
        { "VK_LWIN", "左 Win" },
        { "VK_RWIN", "右 Win" },
        { "WIN", "Win" }
    };

    public static string ConvertToFriendlyText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var keyText = value.Trim();


        // 分割"作为分隔符的逗号"（仅前面是非逗号且无空格的逗号）
        var orParts = SeparatorCommaRegex.Split(keyText)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p)) // 过滤空项
            .ToList();

        // 处理每个"或"部分（空格分隔的组合键，包括逗号key）
        var friendlyOrParts = orParts.Select(ProcessValidCombinationKey).Where(processedPart => !string.IsNullOrEmpty(processedPart)).ToList();

        // 特殊情况：全是逗号的输入（允许连续逗号，只要有空格分隔）
        if (friendlyOrParts.Count == 0 && IsAllCommasWithValidSpace(keyText))
            return GetFriendlyText(",");

        return string.Join(" 或 ", friendlyOrParts);
    }

    // 处理合法的组合键（必须用空格分隔，逗号作为key时需空格分隔）
    private static string ProcessValidCombinationKey(string keyPart)
    {
        if (string.IsNullOrWhiteSpace(keyPart))
            return string.Empty;

        // 优先处理"num X"格式（必须有空格）
        var numMatch = NumFormatRegex.Match(keyPart);
        if (numMatch.Success)
        {
            var numText = GetFriendlyText(numMatch.Value);
            var remaining = keyPart.Replace(numMatch.Value, "", StringComparison.OrdinalIgnoreCase).Trim();
            return string.IsNullOrEmpty(remaining)
                ? numText
                : $"{numText} + {ProcessValidCombinationKey(remaining)}";
        }

        // 按空格分割（强制空格分隔，确保逗号key需与其他键用空格隔开）
        var subParts = keyPart.Split([' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p) &&
                        (p == "," || !p.StartsWith("NO_", StringComparison.OrdinalIgnoreCase))) // 保留逗号key
            .ToList();

        // 非法情况：无空格分隔的项（如",A"会被拆分为[",A"]，subParts为空）
        if (subParts.Count == 0)
            return string.Empty;

        // 转换每个合法子键（包括逗号key）
        var friendlySubParts = subParts.Select(GetFriendlyText).ToList();
        return string.Join(" + ", friendlySubParts);
    }

    // 转换单个键
    private static string GetFriendlyText(string keyText)
    {
        if (string.IsNullOrWhiteSpace(keyText))
            return string.Empty;

        var trimmedKey = keyText.Trim();

        // 处理num格式
        if (NumFormatRegex.IsMatch(trimmedKey))
        {
            var digit = Regex.Match(trimmedKey, @"\d+").Value;
            return $"小键盘 {digit}";
        }

        // 字典匹配（包括逗号key）
        if (VirtualKeyMappings.TryGetValue(trimmedKey, out var friendlyText))
            return friendlyText;

        // VK_前缀转换
        if (!trimmedKey.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) &&
            VirtualKeyMappings.TryGetValue("VK_" + trimmedKey, out friendlyText))
            return friendlyText;

        if (trimmedKey.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) &&
            VirtualKeyMappings.TryGetValue(trimmedKey[3..], out friendlyText))
            return friendlyText;

        return trimmedKey;
    }

    // 判断是否全是逗号且格式合法（允许连续逗号，只要有空格分隔）
    public static bool IsAllCommasWithValidSpace(string input)
    {
        var trimmed = input.Trim();
        return trimmed.All(c => c is ',' or ' ' or '，') && // 只包含逗号、空格
               trimmed.Split([' '], StringSplitOptions.RemoveEmptyEntries)
                   .All(p => p.All(c => c is ',' or '，')); // 空格分隔的部分全是逗号
    }

    // 匹配"作为分隔符的逗号"（前面是非逗号且无空格，后面可接任意）
    public static readonly Regex SeparatorCommaRegex = new(
        @"(?<=[^,，\s])[,，]", // 正后顾：前面必须是非逗号且非空格
        RegexOptions.Compiled
    );

    // 匹配"num X"格式
    public static readonly Regex NumFormatRegex = new(
        @"^num\s+\d+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static bool IsNumFormat(string keyText)
    {
        return NumFormatRegex.IsMatch(keyText);
    }


    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is string str ? ConvertToFriendlyText(str) : value;
    }


    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔值反转后转换为可见性转换器
/// </summary>
public class BoolInverterToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return !boolValue ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        return Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 字符串转换为可见性转换器（非空字符串显示，空字符串隐藏）
/// </summary>
public class StringToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string str = value as string ?? string.Empty;
        bool isVisible = !string.IsNullOrWhiteSpace(str);
        // 如果参数是 "Invert"，则反转可见性
        if (parameter is string and "Invert")
        {
            isVisible = !isVisible;
        }

        return isVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public sealed partial class ModPane : UserControl
{
    public ModPane()
    {
        InitializeComponent();
        Unloaded += ModPane_Unloaded;
    }

    private void ModPane_Unloaded(object sender, RoutedEventArgs e)
    {
        // 断开所有可能的引用，帮助GC回收
        ViewModel = null!;
        ModDetailsPaneImage.ImageUri = null!;
    }


    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ModPaneVM), typeof(ModPane), new PropertyMetadata(default(ModPaneVM)));

    public ModPaneVM ViewModel
    {
        get => (ModPaneVM)GetValue(ViewModelProperty);
        set
        {
            SetValue(ViewModelProperty, value);
            OnViewModelSetHandler(ViewModel);
        }
    }


    private void OnViewModelSetHandler(ModPaneVM viewModel)
    {
    }


    private async void PaneImage_OnDragEnter(object sender, DragEventArgs e)
    {
        if (ViewModel.IsReadOnly || ViewModel.BusySetter.IsHardBusy)
            return;
        var deferral = e.GetDeferral();

        if (e.DataView.Contains(StandardDataFormats.WebLink))
        {
            var url = await e.DataView.GetWebLinkAsync();
            var isValidHttpLink = ViewModel.CanSetImageFromDragDropWeb(url);
            if (isValidHttpLink)
                e.AcceptedOperation = DataPackageOperation.Copy;
        }
        else if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            var data = await e.DataView.GetStorageItemsAsync();
            if (ViewModel.CanSetImageFromDragDropStorageItem(data))
                e.AcceptedOperation = DataPackageOperation.Copy;
        }

        deferral.Complete();
    }

    private async void PaneImage_OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel.IsReadOnly || ViewModel.BusySetter.IsHardBusy)
            return;

        var deferral = e.GetDeferral();
        if (e.DataView.Contains(StandardDataFormats.Uri))
        {
            var uri = await e.DataView.GetUriAsync();
            await ViewModel.SetImageFromDragDropWeb(uri);
        }
        else if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            await ViewModel.SetImageFromDragDropFile(await e.DataView.GetStorageItemsAsync());
        }

        deferral.Complete();
    }

    private bool _isHelpExpanded = false;

    private void OnKeyswapHelpToggleClicked(object sender, RoutedEventArgs e)
    {
        _isHelpExpanded = !_isHelpExpanded;

        if (_isHelpExpanded)
        {
            HelpDetails.Height = double.NaN;
            ExpandIconTransform.Angle = 180;
        }
        else
        {
            HelpDetails.Height = 0;
            ExpandIconTransform.Angle = 0;
        }
    }
}