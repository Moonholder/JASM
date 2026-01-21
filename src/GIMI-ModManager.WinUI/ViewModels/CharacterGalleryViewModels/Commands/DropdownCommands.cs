using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Services.Notifications;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.System;
using WinUI3Localizer;

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
            Content = _localizer.GetLocalizedStringOrDefault("/CharacterGalleryPage/DeleteDialog_DoNotAskAgain", "Do not ask again"),
            IsChecked = false,
        };
        var stackPanel = new StackPanel()
        {
            Children =
            {
                new TextBlock()
                {
                    Text = string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterGalleryPage/DeleteDialog_ConfirmMessage", "Are you sure you want to delete {0}?"), vm.Name),
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.WrapWholeWords,
                },
                doNotAskAgainCheckBox
            }
        };

        var dialog = new ContentDialog()
        {
            Title = _localizer.GetLocalizedStringOrDefault("/CharacterGalleryPage/DeleteDialog_Title", "Delete Mod"),
            Content = stackPanel,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/CharacterGalleryPage/DeleteDialog_DeleteButton", "Delete"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/CharacterGalleryPage/DeleteDialog_CancelButton", "Cancel"),
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

            if (vm.IsEnabled && AutoSync3DMigotoConfig)
            {
                try
                {
                    await _elevatorService.RefreshGenshinMods();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to refresh game after deleting mods");
                }
            }

            await ReloadModsAsync();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to delete mod");
            notificationManager.ShowNotification(
               _localizer.GetLocalizedStringOrDefault("/CharacterGalleryPage/DeleteNotification_FailureTitle", "Failed to delete mod"),
               e.Message, TimeSpan.FromSeconds(5));
            return;
        }

        notificationManager.ShowNotification(
           _localizer.GetLocalizedStringOrDefault("/CharacterGalleryPage/DeleteNotification_SuccessTitle", "Mod deleted successfully"),
           string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterGalleryPage/DeleteNotification_SuccessMessage", "{0} has been removed from the list."), vm.Name),
           TimeSpan.FromSeconds(5));
    }
}