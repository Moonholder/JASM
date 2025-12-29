using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.WinUI.Models.Settings;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterGalleryViewModels;

public partial class CharacterGalleryViewModel
{
    private bool CanToggleSingleSelection()
    {
        return !IsNavigating && !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanToggleSingleSelection))]
    private async Task ToggleSingleSelection()
    {
        var settings = await _localSettingsService
            .ReadOrCreateSettingAsync<CharacterGallerySettings>(CharacterGallerySettings.Key);

        settings.IsSingleSelection = !settings.IsSingleSelection;

        await _localSettingsService.SaveSettingAsync(CharacterGallerySettings.Key, settings);

        IsSingleSelection = settings.IsSingleSelection;
    }

    [RelayCommand(CanExecute = nameof(CanToggleAutoSync))]
    private async Task ToggleAutoSync()
    {
        AutoSync3DMigotoConfig = !AutoSync3DMigotoConfig;
        var settings = await _localSettingsService.ReadOrCreateSettingAsync<ModPresetSettings>(ModPresetSettings.Key);
        AutoSync3DMigotoConfig = settings.AutoSyncMods = AutoSync3DMigotoConfig;
        await _localSettingsService.SaveSettingAsync(ModPresetSettings.Key, settings);
    }

    private bool CanSetHeightWidth(SetHeightWidth _)
    {
        return !IsNavigating && !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanSetHeightWidth))]
    private async Task SetHeightWidth(SetHeightWidth setHeightWidth)
    {
        var settings = await _localSettingsService
            .ReadOrCreateSettingAsync<CharacterGallerySettings>(CharacterGallerySettings.Key);

        settings.ItemHeight = setHeightWidth.Height;
        settings.ItemDesiredWidth = setHeightWidth.Width;

        await _localSettingsService.SaveSettingAsync(CharacterGallerySettings.Key, settings);


        GridItemHeight = settings.ItemHeight;
        GridItemWidth = settings.ItemDesiredWidth;
    }
}

public class SetHeightWidth
{
    public SetHeightWidth(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Height { get; set; }

    public int Width { get; set; }
}