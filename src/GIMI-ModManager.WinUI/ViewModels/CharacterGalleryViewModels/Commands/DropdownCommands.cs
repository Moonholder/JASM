using Windows.System;
using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.Core.Helpers;
using Microsoft.UI.Xaml.Controls;
using GIMI_ModManager.WinUI.Services.Notifications;
using Windows.Storage;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Models.Settings;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterGalleryViewModels;

public partial class CharacterGalleryViewModel
{
    private bool CanOpenModFolder(ModGridItemVm? vm) =>
        vm is not null && !IsNavigating && !IsBusy && !vm.FolderPath.IsNullOrEmpty() && Directory.Exists(vm.FolderPath);

    [RelayCommand(CanExecute = nameof(CanOpenModFolder))]
    private async Task OpenModFolder(ModGridItemVm vm)
    {
        await Launcher.LaunchFolderPathAsync(vm.FolderPath);
    }


    private bool CanOpenModUrl(ModGridItemVm? vm) => vm is not null && !IsNavigating && !IsBusy && vm.HasModUrl;

    [RelayCommand(CanExecute = nameof(CanOpenModUrl))]
    private async Task OpenModUrl(ModGridItemVm vm)
    {
        await Launcher.LaunchUriAsync(vm.ModUrl);
    }

    /// <summary>
    /// return the result of the dialog and if the checkbox "Do not ask again" is checked
    /// </summary>
    private async Task<(ContentDialogResult, bool)> PromptDeleteDialog(ModGridItemVm vm)
    {
        var windowManager = App.GetService<IWindowManagerService>();

        var doNotAskAgainCheckBox = new CheckBox()
        {
            Content = "下次不再询问",
            IsChecked = false,
        };
        var stackPanel = new StackPanel()
        {
            Children =
            {
                new TextBlock()
                {
                    Text = $"您确定要删除 {vm.Name}吗?",
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.WrapWholeWords,
                },
                doNotAskAgainCheckBox
            }
        };

        var dialog = new ContentDialog()
        {
            Title = "删除模组",
            Content = stackPanel,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        // get result and check if checkbox is checked
        var result = await windowManager.ShowDialogAsync(dialog);
        var doNotAskAgain = doNotAskAgainCheckBox.IsChecked == true;

        return (result, doNotAskAgain);
    }

    [RelayCommand(CanExecute = nameof(CanOpenModFolder))]
    private async Task DeleteMod(ModGridItemVm vm)
    {
        if (_modList is null) { return; }

        var notificationManager = App.GetService<NotificationManager>();
        var settings =
            await _localSettingsService
                .ReadOrCreateSettingAsync<CharacterGallerySettings>(CharacterGallerySettings.Key);

        if (settings.CanDeleteDialogPrompt)
        {
            var (result, doNotAskAgainChecked) = await PromptDeleteDialog(vm);
            if (doNotAskAgainChecked)
            {
                settings.CanDeleteDialogPrompt = false;
                await _localSettingsService.SaveSettingAsync(CharacterGallerySettings.Key, settings);
            }

            if (result != ContentDialogResult.Primary)
            {
                return;
            }
        }

        try
        {
            _modList.DeleteModBySkinEntryId(vm.Id);
            await ReloadModsAsync();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to delete mod");
            notificationManager.ShowNotification("删除模组失败", e.Message, TimeSpan.FromSeconds(10));
            return;
        }

        notificationManager.ShowNotification("模组删除成功", $"{vm.Name} 已从列表中删除。", TimeSpan.FromSeconds(5));
    }
}