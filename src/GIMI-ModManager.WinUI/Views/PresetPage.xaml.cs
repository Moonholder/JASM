using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.ViewModels;
using GIMI_ModManager.WinUI.Views.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GIMI_ModManager.WinUI.Views;

public sealed partial class PresetPage : Page
{
    public PresetViewModel ViewModel { get; } = App.GetService<PresetViewModel>();

    public PresetPage()
    {
        InitializeComponent();
        PresetsList.DragItemsCompleted += PresetsList_DragItemsCompleted;
    }

    private async void PresetsList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult == DataPackageOperation.Move && ViewModel.ReorderPresetsCommand.CanExecute(null))
        {
            await ViewModel.ReorderPresetsCommand.ExecuteAsync(null);
        }
    }

    private async void UIElement_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var presetVm = (ModPresetVm)((EditableTextBlock)sender).DataContext;

        if (e.Key == VirtualKey.Enter && ViewModel.RenamePresetCommand.CanExecute(presetVm))
        {
            await ViewModel.RenamePresetCommand.ExecuteAsync(presetVm);
        }
    }

    private TextBlock CreateTextBlock(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true
        };
    }

    private async void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "预设是如何工作的",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            Content = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    CreateTextBlock(
                        "预设是一个要启用的模组列表及其偏好设置。JASM 会在模组本身的 .JASM_ModConfig.json 文件中读取和存储模组偏好设置"),
                    CreateTextBlock(
                        "当你创建一个新的预设时，JASM 会生成一个所有已启用模组及其存储偏好设置的列表。因此，当你稍后应用该预设时，它只会启用这些模组并应用存储在预设中的偏好设置"),

                    CreateTextBlock(
                        "你可以通过启动 Elevator 并勾选自动同步复选框来让 JASM 处理 3Dmigoto 的重新加载。你也可以选择手动处理，勾选显示手动控制复选框，手动保存/加载偏好设置，并通过 F10 键刷新 3Dmigoto."),

                    CreateTextBlock(
                        "可以简单地忽略本页面的预设部分，仅使用手动控制来保存模组偏好设置."
                    )
                }
            }
        };

        await App.GetService<IWindowManagerService>().ShowDialogAsync(dialog).ConfigureAwait(false);
    }

    private void DragHandleIcon_OnPointerEntered(object sender, PointerRoutedEventArgs e) =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);

    private void DragHandleIcon_OnPointerExited(object sender, PointerRoutedEventArgs e) =>
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
}