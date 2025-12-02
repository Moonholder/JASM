using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.UI.Controls;
using GIMI_ModManager.WinUI.Models.Options;
using GIMI_ModManager.WinUI.ViewModels.SubVms;
using GIMI_ModManager.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.ObjectModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels;

public partial class CharacterDetailsViewModel
{
    [ObservableProperty] private bool _isSingleSelectEnabled;
    [ObservableProperty] private bool _isModFolderNameColumnVisible;
    [ObservableProperty] private bool _isSingleModSelected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddModArchiveCommand), nameof(AddModFolderCommand))]
    private bool _isAddingModFolder;

    private DispatcherQueue DispatcherQueue =>
        App.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();


    private async Task InitToolbarAsync()
    {
        var settings = await ReadSettingsAsync();
        IsSingleSelectEnabled = settings.SingleSelect;
        ModGridVM.GridSelectionMode = IsSingleSelectEnabled ? DataGridSelectionMode.Single : DataGridSelectionMode.Extended;
        IsModFolderNameColumnVisible = settings.ModFolderNameColumnVisible;
    }

    [RelayCommand]
    private async Task OpenGIMIRootFolderAsync()
    {
        var options = await _localSettingsService.ReadSettingAsync<ModManagerOptions>(ModManagerOptions.Section) ??
                      new ModManagerOptions();
        if (string.IsNullOrWhiteSpace(options.GimiRootFolderPath)) return;
        await Launcher.LaunchFolderAsync(
            await StorageFolder.GetFolderFromPathAsync(options.GimiRootFolderPath));
    }

    [RelayCommand]
    private async Task OpenCharacterFolderAsync()
    {
        var directoryToOpen = new DirectoryInfo(_modList.AbsModsFolderPath);
        if (!directoryToOpen.Exists)
        {
            _modList.InstantiateCharacterFolder();
            directoryToOpen.Refresh();

            if (!directoryToOpen.Exists)
            {
                var parentDir = directoryToOpen.Parent;

                if (parentDir is null)
                {
                    _logger.Error("Could not find parent directory of {Directory}", directoryToOpen.FullName);
                    return;
                }

                directoryToOpen = parentDir;
            }
        }

        await Launcher.LaunchFolderAsync(
            await StorageFolder.GetFolderFromPathAsync(directoryToOpen.FullName));
    }


    [RelayCommand]
    private async Task OpenModFolderAsync()
    {
        if (ModGridVM.SelectedMods.Count != 1) return;

        var mod = ModGridVM.SelectedMods.First();
        var directoryToOpen = new DirectoryInfo(mod.AbsFolderPath);
        if (!directoryToOpen.Exists)
        {
            _logger.Error("Could not find directory {Directory}", directoryToOpen.FullName);
            return;
        }

        await Launcher.LaunchFolderAsync(
            await StorageFolder.GetFolderFromPathAsync(directoryToOpen.FullName));
    }

    [RelayCommand(CanExecute = nameof(IsNotHardBusy))]
    private async Task RefreshAllModsAsync()
    {
        await CommandWrapperAsync(true,
            () => ModGridVM.ReloadAllModsAsync(minimumWaitTime: TimeSpan.FromMilliseconds(300))).ConfigureAwait(false);
    }


    [RelayCommand(CanExecute = nameof(IsNotHardBusy))]
    private async Task ToggleSingleSelectAsync()
    {
        await CommandWrapperAsync(false, async () =>
        {
            var settings = await ReadSettingsAsync();
            settings.SingleSelect = !IsSingleSelectEnabled;
            await SaveSettingsAsync(settings);

            IsSingleSelectEnabled = settings.SingleSelect;

            var firstModSelected = ModGridVM.SelectedMods.FirstOrDefault();
            ModGridVM.GridSelectionMode = IsSingleSelectEnabled ? DataGridSelectionMode.Single : DataGridSelectionMode.Extended;
            if (firstModSelected is not null)
                ModGridVM.SetSelectedMod(firstModSelected.Id);
        }).ConfigureAwait(false);
    }


    [RelayCommand]
    private async Task ToggleHideModFolderColumnAsync()
    {
        await CommandWrapperAsync(false, async () =>
        {
            var settings = await ReadSettingsAsync();
            settings.ModFolderNameColumnVisible = !IsModFolderNameColumnVisible;
            await SaveSettingsAsync(settings);

            IsModFolderNameColumnVisible = settings.ModFolderNameColumnVisible;
            ModGridVM.IsModFolderNameColumnVisible = settings.ModFolderNameColumnVisible;
        }).ConfigureAwait(false);
    }


    private bool CanAddModFolder() => !IsAddingModFolder && IsNotHardBusy;

    [RelayCommand(CanExecute = nameof(CanAddModFolder))]
    private async Task AddModFolder()
    {
        await CommandWrapperAsync(true, async () =>
        {
            var pathPicker = new PathPicker();
            await pathPicker.BrowseFolderPathAsync(App.MainWindow);
            if (string.IsNullOrEmpty(pathPicker.Path))
            {
                return;
            }
            var folder = await StorageFolder.GetFolderFromPathAsync(pathPicker.Path);

            if (folder is null)
            {
                _logger.Debug("User cancelled folder picker.");
                return;
            }

            try
            {
                IsAddingModFolder = true;
                var result = await Task.Run(async () =>
                {
                    var installMonitor = await _modDragAndDropService.AddStorageItemFoldersAsync(_modList,
                        new ReadOnlyCollection<IStorageItem>([folder]), SelectedSkin).ConfigureAwait(false);

                    if (installMonitor is not null)
                        return await installMonitor.WaitForCloseAsync().ConfigureAwait(false);
                    return null;
                }, CancellationToken.None);


                if (result?.CloseReason == CloseRequestedArgs.CloseReasons.Success)
                    await ModGridVM.ReloadAllModsAsync();
            }
            finally
            {
                IsAddingModFolder = false;
            }
        }).ConfigureAwait(false);
    }

    private bool CanAddModArchive() => !IsAddingModFolder && IsNotHardBusy;

    [RelayCommand(CanExecute = nameof(CanAddModArchive))]
    private async Task AddModArchiveAsync()
    {
        await CommandWrapperAsync(true, async () =>
        {
            var pathPicker = new PathPicker()
            {
                FileTypeFilter = [".zip", ".rar", ".7z"]
            };
            await pathPicker.BrowseFilePathAsync(App.MainWindow);
            if (string.IsNullOrEmpty(pathPicker.Path))
            {
                return;
            }
            var file = await StorageFile.GetFileFromPathAsync(pathPicker.Path);
            if (file is null)
            {
                _logger.Debug("User cancelled file picker.");
                return;
            }

            try
            {
                IsAddingModFolder = true;
                var result = await Task.Run(async () =>
                    {
                        var installMonitor = await _modDragAndDropService.AddStorageItemFoldersAsync(_modList, [file], SelectedSkin).ConfigureAwait(false);

                        if (installMonitor is not null)
                            return await installMonitor.WaitForCloseAsync().ConfigureAwait(false);
                        return null;
                    },
                    CancellationToken.None);

                if (result?.CloseReason == CloseRequestedArgs.CloseReasons.Success)
                    await ModGridVM.ReloadAllModsAsync();

            }
            catch (Exception e)
            {
                _logger.Error(e, "Error while adding archive.");
                _notificationService.ShowNotification("Error while adding storage items.",
                    $"An error occurred while adding the storage items.\n{e.Message}",
                    TimeSpan.FromSeconds(5));
            }
            finally
            {
                IsAddingModFolder = false;
            }
        }).ConfigureAwait(false);
    }
    private bool CanAddModClipboard()
        => !IsAddingModFolder && IsNotHardBusy;

    [RelayCommand(CanExecute = nameof(CanAddModClipboard))]
    private async Task AddModFromClipboardAsync()
    {
        var package = Clipboard.GetContent();
        if (package is null) return;

        if (package.Contains(StandardDataFormats.Text))
        {
            var text = await package.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) return;

            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && CanDragDropModUrl(uri))
            {
                await CommandWrapperAsync(true, async () =>
                {
                    try
                    {
                        var windowKey = $"ModPage_{_modList.Character.InternalName}";
                        if (_windowManagerService.GetWindow(windowKey) is { } window)
                        {
                            DispatcherQueue.TryEnqueue(() => window.Activate());
                            return;
                        }

                        var modWindow = new GbModPageWindow(uri, _modList.Character);
                        _windowManagerService.CreateWindow(modWindow, identifier: windowKey);
                        await Task.Delay(100);
                        modWindow.BringToFront();
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Error opening mod page window from clipboard.");
                        _notificationService.ShowNotification("打开模组页面窗口时出错", e.Message, TimeSpan.FromSeconds(5));
                    }
                }).ConfigureAwait(false);
            }
            else
            {
                _notificationService.ShowNotification("无法添加模组", "无法从剪切板中获取有效的GameBanana模组链接。", TimeSpan.FromSeconds(5));
            }
        }
        else if (package.Contains(StandardDataFormats.StorageItems))
        {
            await CommandWrapperAsync(true, async () =>
            {
                try
                {
                    var items = await package.GetStorageItemsAsync();
                    if (items is null || items.Count == 0) return;
                    IsAddingModFolder = true;
                    var result = await Task.Run(async () =>
                    {
                        var installMonitor = await _modDragAndDropService.AddStorageItemFoldersAsync(_modList, items, SelectedSkin).ConfigureAwait(false);
                        if (installMonitor is not null)
                            return await installMonitor.WaitForCloseAsync().ConfigureAwait(false);
                        return null;
                    }, CancellationToken.None);


                    if (result?.CloseReason == CloseRequestedArgs.CloseReasons.Success)
                    {
                        // DispatcherQueue.TryEnqueue(async () =>
                        // {
                        await ModGridVM.ReloadAllModsAsync();
                        // });
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Error while adding storage items.");
                    _notificationService.ShowNotification("添加模组时出错.", e.Message, TimeSpan.FromSeconds(5));
                }
                finally
                {
                    IsAddingModFolder = false;
                }
            }).ConfigureAwait(false);
        }
    }
}