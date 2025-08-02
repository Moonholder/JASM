using GIMI_ModManager.Core.Entities.Mods.FileModels;
using GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
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
        { "VK_SEPARATOR", "小键盘 ," },
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
        { "VK_BACK", "退格" },
        { "VK_DELETE", "删除" },
        { "VK_INSERT", "插入" },
        { "VK_HOME", "Home" },
        { "VK_END", "End" },
        { "VK_PRIOR", "Page Up" },
        { "VK_NEXT", "Page Down" },
        { "VK_SLASH", "/" },
        { "VK_COMMA", "," },
        { "VK_TILDE", "~" },
        { "VK_LBUTTON", "鼠标左键"},
        { "VK_RBUTTON", "鼠标右键"},
        { "VK_MBUTTON", "鼠标中键"},
        { "VK_XBUTTON1", "X1 鼠标侧键"},
        { "VK_XBUTTON2", "X2 鼠标侧键"},
        
        
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
        
        // 修饰符前缀
        { "MODIFIERS", "修饰键" },
        { "ALT", "Alt" },
        { "SHIFT", "Shift" },
        { "CTRL", "Ctrl" },
        { "RCTRL", "右Ctrl" },
        { "WIN", "Win" }
    };

    public static string ConvertToFriendlyText(string value)
    {
        if (value is not string keyText || string.IsNullOrWhiteSpace(keyText))
            return value;

        // 首先按逗号分割，处理多个独立的按键组合
        var commaParts = keyText.Split([','], StringSplitOptions.RemoveEmptyEntries)
                               .Select(p => p.Trim())
                               .Where(p => !string.IsNullOrEmpty(p))
                               .ToList();

        var friendlyParts = new List<string>();

        foreach (var commaPart in commaParts)
        {
            // 对于每个逗号分隔的部分，检查是否包含空格（组合按键）
            if (commaPart.Contains(' '))
            {
                // 这是组合按键，按空格分割并转换为友好文本
                var spaceParts = commaPart.Split([' '], StringSplitOptions.RemoveEmptyEntries)
                                        .Select(p => p.Trim())
                                        .Where(p => !string.IsNullOrEmpty(p))
                                        .ToList();

                var combinationParts = new List<string>();
                foreach (var spacePart in spaceParts)
                {
                    var friendlyText = GetFriendlyText(spacePart);
                    combinationParts.Add(friendlyText);
                }

                // 组合按键用 + 连接
                friendlyParts.Add(string.Join(" + ", combinationParts));
            }
            else
            {
                // 单个按键
                var friendlyText = GetFriendlyText(commaPart);
                friendlyParts.Add(friendlyText);
            }
        }

        // 多个独立按键组合用 " 或 " 连接
        return string.Join(" 或 ", friendlyParts);
    }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is string str ? ConvertToFriendlyText(str) : value;
    }

    private static string GetFriendlyText(string keyText)
    {
        // 尝试直接匹配
        if (VirtualKeyMappings.TryGetValue(keyText, out var friendlyText))
        {
            return friendlyText;
        }
        // 尝试添加 VK_ 前缀匹配
        else if (!keyText.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) &&
                 VirtualKeyMappings.TryGetValue("VK_" + keyText, out var friendlyTextWithVk))
        {
            return friendlyTextWithVk;
        }
        // 尝试移除 VK_ 前缀匹配
        else if (keyText.StartsWith("VK_", StringComparison.OrdinalIgnoreCase) &&
                 VirtualKeyMappings.TryGetValue(keyText[3..], out var friendlyTextWithoutVk))
        {
            return friendlyTextWithoutVk;
        }
        else if (keyText.StartsWith("NO_", StringComparison.OrdinalIgnoreCase) &&
             VirtualKeyMappings.TryGetValue(keyText[3..], out var friendlyTextWithoutNO))
        {
            return "不按" + friendlyTextWithoutNO;
        }
        else
        {
            return keyText;
        }
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

public sealed partial class ModPane : UserControl
{
    public ModPane()
    {
        InitializeComponent();
    }


    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ModPaneVM), typeof(ModPane), new PropertyMetadata(default(ModPaneVM)));

    public ModPaneVM ViewModel
    {
        get { return (ModPaneVM)GetValue(ViewModelProperty); }
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