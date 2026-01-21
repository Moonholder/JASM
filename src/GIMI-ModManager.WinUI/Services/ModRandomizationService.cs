using System;
using System.Linq;
using System.Threading.Tasks;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.Services.ModHandling;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace GIMI_ModManager.WinUI.Services;

public class ModRandomizationService
{
    private readonly IGameService _gameService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly IWindowManagerService _windowManagerService;
    private readonly CharacterSkinService _characterSkinService;
    private readonly ElevatorService _elevatorService;
    private readonly NotificationManager _notificationManager;
    private readonly ILogger _logger;
    private readonly ILanguageLocalizer _localizer;
    private static readonly Random Random = new();

    public ModRandomizationService(
        IGameService gameService,
        ISkinManagerService skinManagerService,
        IWindowManagerService windowManagerService,
        CharacterSkinService characterSkinService,
        ElevatorService elevatorService,
        NotificationManager notificationManager,
        ILanguageLocalizer localizer,
        ILogger logger)
    {
        _gameService = gameService;
        _skinManagerService = skinManagerService;
        _windowManagerService = windowManagerService;
        _characterSkinService = characterSkinService;
        _elevatorService = elevatorService;
        _notificationManager = notificationManager;
        _localizer = localizer;
        _logger = logger.ForContext<ModRandomizationService>();
    }

    public async Task ShowRandomizeModsDialog()
    {
        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("/PresetPage/RandomizeDialog_Title", "Randomize Enabled Mods"),
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/PresetPage/RandomizeDialog_ConfirmButton", "Randomize"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("/PresetPage/RandomizeDialog_CancelButton", "Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        var categories = _gameService.GetCategories();
        var stackPanel = new StackPanel();

        stackPanel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("/PresetPage/RandomizeDialog_SelectCategoryLabel", "Select categories to randomize:")
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("/PresetPage/RandomizeDialog_WarningText",
                "Note: This will only randomize mod folders that allow only one active mod by design. Therefore, 'Others' folders will not be randomized. Also, only one mod per game character skin will be enabled."),
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 0, 0, 10)
        });

        foreach (var category in categories)
        {
            var checkBox = new CheckBox
            {
                Content = category.DisplayNamePlural,
                IsChecked = true
            };
            stackPanel.Children.Add(checkBox);
        }

        stackPanel.Children.Add(new CheckBox
        {
            Margin = new Thickness(0, 10, 0, 0),
            Content = _localizer.GetLocalizedStringOrDefault("/PresetPage/RandomizeDialog_AllowNoMods", "Allow no mods enabled. This means it is possible for a mod folder to have no mods enabled."),
            IsChecked = false
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = _localizer.GetLocalizedStringOrDefault("/PresetPage/RandomizeDialog_SuggestionText", "If you have a lot of mods enabled, I suggest creating a preset (or backup) of your mods before randomizing."),
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 10, 0, 0)
        });


        dialog.Content = stackPanel;

        var result = await _windowManagerService.ShowDialogAsync(dialog);
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var selectedCategories = stackPanel.Children
            .OfType<CheckBox>()
            .SkipLast(1)
            .Where(c => c.IsChecked == true)
            .Select(c => categories.First(cat => cat.DisplayNamePlural.Equals(c.Content)))
            .ToList();

        var allowNoMods = stackPanel.Children
            .OfType<CheckBox>()
            .Last()
            .IsChecked == true;

        if (selectedCategories.Count == 0)
        {
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/PresetPage/RandomizeDialog_NoCategoryTitle", "No category selected"),
                _localizer.GetLocalizedStringOrDefault("/PresetPage/RandomizeDialog_NoCategoryMessage", "No categories selected for randomization."),
                TimeSpan.FromSeconds(5));
            return;
        }

        try
        {
            await Task.Run(async () =>
            {
                var modLists = _skinManagerService.CharacterModLists
                    .Where(modList => selectedCategories.Contains(modList.Character.ModCategory))
                    .Where(modList => !modList.Character.IsMultiMod)
                    .ToList();

                foreach (var modList in modLists)
                {
                    var mods = modList.Mods.ToList();

                    if (mods.Count == 0)
                        continue;

                    // Need special handling for characters because they have an in game skins
                    if (modList.Character is ICharacter { Skins.Count: > 1 } character)
                    {
                        var skinModMap = await _characterSkinService.GetAllModsBySkinAsync(character)
                            .ConfigureAwait(false);
                        if (skinModMap is null)
                            continue;

                        // Don't know what to do with undetectable mods
                        skinModMap.UndetectableMods.ForEach(mod => modList.DisableMod(mod.Id));

                        foreach (var (_, skinMods) in skinModMap.ModsBySkin)
                        {
                            if (skinMods.Count == 0)
                                continue;

                            foreach (var mod in skinMods.Where(mod => modList.IsModEnabled(mod)))
                            {
                                modList.DisableMod(mod.Id);
                            }

                            var randomModIndex = Random.Next(0, skinMods.Count + (allowNoMods ? 1 : 0));

                            if (randomModIndex == skinMods.Count)
                                continue;

                            modList.EnableMod(skinMods.ElementAt(randomModIndex).Id);
                        }

                        continue;
                    }

                    foreach (var characterSkinEntry in mods.Where(characterSkinEntry => characterSkinEntry.IsEnabled))
                    {
                        modList.DisableMod(characterSkinEntry.Id);
                    }

                    var randomIndex = Random.Next(0, mods.Count + (allowNoMods ? 1 : 0));
                    if (randomIndex == mods.Count)
                        continue;

                    modList.EnableMod(mods[randomIndex].Id);
                }
            });
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to randomize mods");
            _notificationManager.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/PresetPage/RandomizeDialog_FailedTitle", "Randomization failed"),
                e.Message, TimeSpan.FromSeconds(5));
            return;
        }

        if (_elevatorService.ElevatorStatus == ElevatorStatus.Running)
        {
            await Task.Run(() => _elevatorService.RefreshGenshinMods());
        }

        var successMsgPrefix = _localizer.GetLocalizedStringOrDefault("/PresetPage/RandomizeDialog_SuccessMessagePrefix", "Completed randomization for categories: ");
        _notificationManager.ShowNotification(
            _localizer.GetLocalizedStringOrDefault("/PresetPage/RandomizeDialog_SuccessTitle", "Mods randomized"),
            successMsgPrefix + string.Join(", ", selectedCategories.Select(c => c.DisplayNamePlural)),
            TimeSpan.FromSeconds(5));
    }
}