using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.WinUI.Contracts.Services;
using System.IO;

namespace GIMI_ModManager.WinUI.Views.Dialogs;

public sealed partial class GameBananaInstallTargetDialog : ContentDialog
{
    private readonly ILanguageLocalizer _localizer;
    private readonly IGameService _gameService;
    private readonly string? _modName;
    private readonly string? _fileName;
    private readonly List<Tuple<IModdableObject, string>> _displayItems;

    public IModdableObject? SelectedTarget => (TargetListView.SelectedItem as Tuple<IModdableObject, string>)?.Item1;

    public GameBananaInstallTargetDialog(ILanguageLocalizer localizer, IGameService gameService, string? modName, string? fileName, List<Tuple<IModdableObject, string>> displayItems)
    {
        this.InitializeComponent();

        _localizer = localizer;
        _gameService = gameService;
        _modName = modName;
        _fileName = fileName;
        _displayItems = displayItems;

        PromptTextBlock.Text = string.Format(_localizer.GetLocalizedStringOrDefault("/GameBananaPage/MatchCategoryPrompt", "Unable to automatically match target category for \"{0}\", please select manually:"), modName);

        TargetListView.ItemsSource = _displayItems;
        if (_displayItems.Count > 0)
        {
            TargetListView.SelectedIndex = 0;
        }

        this.Title = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/SelectInstallTarget", "Select Install Target");
        this.PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/DialogOk", "OK");
        this.CloseButtonText = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/DialogCancel", "Cancel");
        SearchBox.PlaceholderText = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/SearchCategoryPlaceholder", "Search category or name...");
        StrongMatchBtn.Content = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StrongMatchBtn", "Can't find? Try matching based on file name");
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var query = sender.Text?.Trim();
            if (string.IsNullOrEmpty(query))
            {
                TargetListView.ItemsSource = _displayItems;
            }
            else
            {
                TargetListView.ItemsSource = _displayItems
                    .Where(n => n.Item2.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }
    }

    private void StrongMatchBtn_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var matchScores = new Dictionary<IModdableObject, int>();
        void UpdateScores(string? query)
        {
            if (string.IsNullOrWhiteSpace(query)) return;
            var dict = _gameService.QueryModdableObjects(query);
            foreach (var kv in dict)
            {
                if (!matchScores.TryGetValue(kv.Key, out var existingScore) || kv.Value > existingScore)
                {
                    matchScores[kv.Key] = kv.Value;
                }
            }
        }

        UpdateScores(_modName);
        if (!string.IsNullOrEmpty(_fileName))
        {
            var fileNameNoExt = Path.GetFileNameWithoutExtension(_fileName).Replace("_", " ");
            UpdateScores(fileNameNoExt);
        }

        var selectedTuple = (Tuple<IModdableObject, string>?)null;

        if (matchScores.Count > 0)
        {
            var bestMatch = matchScores.OrderByDescending(x => x.Value).First();
            if (bestMatch.Value > 0)
            {
                selectedTuple = _displayItems.FirstOrDefault(t => t.Item1.InternalName.Equals(bestMatch.Key.InternalName));
            }
        }

        if (selectedTuple == null)
        {
            selectedTuple = _displayItems.FirstOrDefault(t => t.Item1.InternalName.Id.Contains("Others", StringComparison.OrdinalIgnoreCase));
        }

        if (selectedTuple != null)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = string.Empty;
            TargetListView.SelectedItem = selectedTuple;
            TargetListView.ScrollIntoView(selectedTuple);
        }
    }
}