using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.Core.Entities;
using GIMI_ModManager.Core.GamesService.Interfaces;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterGalleryViewModels;

public partial class CharacterGalleryViewModel
{
    private bool CanToggleMod(ModGridItemVm? thisMod)
    {
        return !IsNavigating && !IsBusy && thisMod is not null;
    }

    // This function is called from the ModModel _toggleMod delegate.
    // This is a hacky way to get the toggle button to work.
    [RelayCommand(CanExecute = nameof(CanToggleMod))]
    private async Task ToggleMod(ModGridItemVm thisMod)
    {
        if (IsNavigating) return;

        IsBusy = true;
        try
        {
            CharacterSkinEntry modEntryToToggle = null!;
            var modsInScope = new List<CharacterSkinEntry>();

            await Task.Run(async () =>
            {
                var allSkinMods = _modList.Mods;
                modEntryToToggle = allSkinMods.First(m => m.Id == thisMod.Id);

                if (_moddableObject is ICharacter { Skins.Count: > 1 })
                {
                    var selectedSkin = _selectedSkin!;
                    var skinEntries = _characterSkinService.GetModsForSkinAsync(selectedSkin);
                    await foreach (var skinEntry in skinEntries.ConfigureAwait(false))
                    {
                        var mod = allSkinMods.FirstOrDefault(m => m.Id == skinEntry.Id);
                        if (mod is not null) modsInScope.Add(mod);
                    }
                }
                else
                {
                    modsInScope.AddRange(allSkinMods);
                }
            });

            var shouldEnable = !modEntryToToggle.IsEnabled;
            var modsToDisable = (shouldEnable && IsSingleSelection)
                ? modsInScope.Where(m => m.Id != modEntryToToggle.Id && m.IsEnabled).ToList()
                : new List<CharacterSkinEntry>();

            var modsToSync = new List<CharacterSkinEntry>();
            if (AutoSync3DMigotoConfig)
            {
                modsToSync.AddRange(modsToDisable);
                if (modEntryToToggle.IsEnabled) modsToSync.Add(modEntryToToggle);
            }

            foreach (var skinEntry in modsToDisable)
            {
                if (skinEntry.IsEnabled) _modList.DisableMod(skinEntry.Id);
            }
            if (shouldEnable) _modList.EnableMod(modEntryToToggle.Id);
            else _modList.DisableMod(modEntryToToggle.Id);

            if (AutoSync3DMigotoConfig)
            {
                try
                {
                    await _elevatorService.RefreshGenshinMods();

                    if (modsToSync.Count > 0)
                    {
                        await Task.Delay(50);
                        await Parallel.ForEachAsync(modsToSync, async (mod, ct) =>
                        {
                            await _userPreferencesService.SyncPreferencesToModLocalFilesAsync(mod.Id);
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during AutoSync sequence");
                }
            }

            await UpdateGridItemAsync(modEntryToToggle);

            foreach (var otherModEntry in modsToDisable)
            {
                await UpdateGridItemAsync(otherModEntry);
            }
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to toggle mod");
        }
        finally
        {
            IsBusy = false;
        }
    }
}