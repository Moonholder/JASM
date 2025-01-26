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
    private static readonly Random Random = new();

    public ModRandomizationService(
        IGameService gameService,
        ISkinManagerService skinManagerService,
        IWindowManagerService windowManagerService,
        CharacterSkinService characterSkinService,
        ElevatorService elevatorService,
        NotificationManager notificationManager,
        ILogger logger)
    {
        _gameService = gameService;
        _skinManagerService = skinManagerService;
        _windowManagerService = windowManagerService;
        _characterSkinService = characterSkinService;
        _elevatorService = elevatorService;
        _notificationManager = notificationManager;
        _logger = logger.ForContext<ModRandomizationService>();
    }

    public async Task ShowRandomizeModsDialog()
    {
        var dialog = new ContentDialog
        {
            Title = "随机启用模组",
            PrimaryButtonText = "随机",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        var categories = _gameService.GetCategories();
        var stackPanel = new StackPanel();

        stackPanel.Children.Add(new TextBlock
        {
            Text = "选择你想要对其模组进行随机化处理的类别:"
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = "注意：此操作只会随机化那些按设计仅允许一个模组处于激活状态的模组文件夹。因此，“Others _” 这类文件夹不会被随机化。并且，每个游戏角色皮肤仅会启用一个模组",
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
            Content = "允许最终不使用任何模组。这意味着某个模组文件夹有可能不启用任何模组",
            IsChecked = false
        });

        stackPanel.Children.Add(new TextBlock
        {
            Text = "如果你启用了大量模组，我建议在进行随机化操作之前，为你的模组创建一个预设（或备份）",
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
            _notificationManager.ShowNotification("未选择任何类别", "未选择任何要进行随机化的类别.",
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
            _notificationManager.ShowNotification("模组随机化失败", e.Message, TimeSpan.FromSeconds(5));
            return;
        }

        if (_elevatorService.ElevatorStatus == ElevatorStatus.Running)
        {
            await Task.Run(() => _elevatorService.RefreshGenshinMods());
        }

        _notificationManager.ShowNotification("已随机启用模组",
            "已针对所选类别完成模组随机化: " +
            string.Join(", ", selectedCategories.Select(c => c.DisplayNamePlural)),
            TimeSpan.FromSeconds(5));
    }
}