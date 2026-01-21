using System.Text;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels;

public partial class CharacterDetailsViewModel
{
    private static bool _removeFromPresetCheckBox = false;
    private static bool _moveToRecycleBinCheckBox = true;

    private record ModToDelete(Guid Id, string DisplayName, string FolderPath, string FolderName)
    {
        public ModToDelete(ModToDelete m, Exception e, string? presetName = null) : this(m.Id, m.DisplayName, m.FolderPath, m.FolderName)
        {
            Exception = e;
            PresetName = presetName;
        }

        public Exception? Exception { get; }
        public string? PresetName { get; }
    }

    private bool CanDeleteMods() => IsNavigationFinished && !IsHardBusy && !IsSoftBusy && ModGridVM.SelectedMods.Count > 0;

    [RelayCommand(CanExecute = nameof(CanDeleteMods))]
    private async Task DeleteModsAsync()
    {
        var selectedMods = ModGridVM.SelectedMods.Select(m => new ModToDelete(m.Id, m.DisplayName, m.AbsFolderPath, m.FolderName)).ToList();

        if (selectedMods.Count == 0)
            return;

        var hasEnabledMods = ModGridVM.SelectedMods.Any(m => m.IsEnabled);
        var shownCharacterName = ShownModObject.DisplayName;
        var selectedModsCount = selectedMods.Count;

        var modsToDeleteErrored = new List<ModToDelete>();
        var modsToDeletePresetError = new List<ModToDelete>();

        var modsDeleted = new List<ModToDelete>(selectedModsCount);

        var moveToRecycleBinCheckBox = new CheckBox()
        {
            Content = _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DeleteDialog_MoveToRecycleBin", "Move to Recycle Bin?"),
            IsChecked = _moveToRecycleBinCheckBox
        };

        var removeFromPresetsCheckBox = new CheckBox()
        {
            Content = _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DeleteDialog_RemoveFromPresets", "Remove from presets?"),
            IsChecked = _removeFromPresetCheckBox
        };


        var mods = new ListView()
        {
            ItemsSource = selectedMods.Select(m => m.DisplayName + " - " + m.FolderName),
            SelectionMode = ListViewSelectionMode.None
        };

        var scrollViewer = new ScrollViewer()
        {
            Content = mods,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 400
        };
        var stackPanel = new StackPanel()
        {
            Children =
            {
                moveToRecycleBinCheckBox,
                removeFromPresetsCheckBox,
                scrollViewer
            }
        };

        var contentWrapper = new Grid()
        {
            MinWidth = 500,
            Children =
            {
                stackPanel
            }
        };

        var dialog = new ContentDialog()
        {
            Title = string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DeleteDialog_Title", "Delete these {0} mods?"), selectedModsCount),
            Content = contentWrapper,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DeleteDialog_DeleteButton", "Delete"),
            SecondaryButtonText = _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DeleteDialog_CancelButton", "Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };


        var result = await _windowManagerService.ShowDialogAsync(dialog);

        var recycleMods = moveToRecycleBinCheckBox.IsChecked == true;
        var removeFromPresets = removeFromPresetsCheckBox.IsChecked == true;
        _moveToRecycleBinCheckBox = recycleMods;
        _removeFromPresetCheckBox = removeFromPresets;


        if (result != ContentDialogResult.Primary)
            return;


        await CommandWrapperAsync(true, async () =>
        {
            await Task.Run(async () =>
            {
                if (removeFromPresets)
                {
                    var modIdToPresetMap = await _presetService.FindPresetsForModsAsync(selectedMods.Select(m => m.Id), CancellationToken.None)
                        .ConfigureAwait(false);

                    foreach (var mod in selectedMods)
                    {
                        if (!modIdToPresetMap.TryGetValue(mod.Id, out var presets)) continue;

                        foreach (var preset in presets)
                        {
                            try
                            {
                                await _presetService.DeleteModEntryAsync(preset.Name, mod.Id, CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception e)
                            {
                                _logger.Error(e, "Error removing mod: {ModName} from preset: {PresetName} | mod path: {ModPath} ", mod.DisplayName,
                                    mod.FolderPath,
                                    preset.Name);
                                modsToDeletePresetError.Add(new ModToDelete(mod, e));
                            }
                        }
                    }
                }

                foreach (var mod in selectedMods)
                {
                    try
                    {
                        _modList.DeleteModBySkinEntryId(mod.Id, recycleMods);
                        modsDeleted.Add(mod);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Error deleting mod {ModName} | {ModPath}", mod.DisplayName, mod.FolderPath);

                        modsToDeleteErrored.Add(new ModToDelete(mod, e));
                    }
                }
            });

            if (hasEnabledMods && AutoSync3DMigotoConfig)
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

            ModGridVM.QueueModRefresh();


            if (modsToDeleteErrored.Count > 0 || modsToDeletePresetError.Count > 0)
            {
                var content = new StringBuilder();

                content.AppendLine(_localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DeleteError_Header", "Error deleting mods:"));


                if (modsToDeletePresetError.Count > 0)
                {
                    content.AppendLine(_localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DeleteError_PresetHeader", "Preset error Mods:"));
                    foreach (var mod in modsToDeletePresetError)
                    {
                        content.AppendLine($"- {mod.DisplayName}");
                        content.AppendLine($"  - {mod.Exception?.Message}");
                        content.AppendLine($"  - {mod.PresetName}");
                    }
                }

                if (modsToDeleteErrored.Count > 0)
                {
                    content.AppendLine(_localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DeleteError_DeleteHeader", "Delete error Mods:"));
                    foreach (var mod in modsToDeleteErrored)
                    {
                        content.AppendLine($"- {mod.DisplayName}");
                        content.AppendLine($"  - {mod.Exception?.Message}");
                    }
                }

                _notificationService.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DeleteNotification_FailureTitle", "Failed to delete mods"),
                    content.ToString(), TimeSpan.FromSeconds(10));
                return;
            }

            _notificationService.ShowNotification(
                string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DeleteNotification_SuccessTitle", "{0} mods deleted"), modsDeleted.Count),
                string.Format(_localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DeleteNotification_SuccessMessage", "Successfully deleted mods for {0}: {1}"),
                    shownCharacterName,
                    string.Join(", ", selectedMods.Select(m => m.DisplayName))),
                TimeSpan.FromSeconds(5));
        }).ConfigureAwait(false);
    }
}