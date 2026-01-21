using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.ViewModels;
using GIMI_ModManager.WinUI.Views.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using GIMI_ModManager.Core.Contracts.Services;

namespace GIMI_ModManager.WinUI.Views;

public sealed partial class PresetPage : Page
{
    public PresetViewModel ViewModel { get; } = App.GetService<PresetViewModel>();

    private readonly ILanguageLocalizer _localizer = App.GetService<ILanguageLocalizer>();

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
            Title = _localizer.GetLocalizedStringOrDefault("/PresetPage/HowPresetsWorkTitle", "How Presets Work"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/PresetPage/CloseButton", "Close"),
            DefaultButton = ContentDialogButton.Close,
            Content = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    CreateTextBlock(
                        _localizer.GetLocalizedStringOrDefault("/PresetPage/HowPresetsWorkText1",
                        "A preset is a list of mods to enable and their preferences. JASM reads and stores mod preferences in the .JASM_ModConfig.json file within the mod itself.")),
                    CreateTextBlock(
                        _localizer.GetLocalizedStringOrDefault("/PresetPage/HowPresetsWorkText2",
                        "When you create a new preset, JASM generates a list of all enabled mods and their stored preferences. So when you apply that preset later, it will only enable those mods and apply the preferences stored in the preset.")),

                    CreateTextBlock(
                        _localizer.GetLocalizedStringOrDefault("/PresetPage/HowPresetsWorkText3",
                        "You can let JASM handle 3Dmigoto reloading by checking the Auto Sync checkbox. You can also choose to handle it manually by checking the Show Manual Controls checkbox, manually saving/loading preferences, and refreshing 3Dmigoto via F10.")),

                    CreateTextBlock(
                        _localizer.GetLocalizedStringOrDefault("/PresetPage/HowPresetsWorkText4",
                        "You can simply ignore the preset part of this page and use manual controls only to save mod preferences.")
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