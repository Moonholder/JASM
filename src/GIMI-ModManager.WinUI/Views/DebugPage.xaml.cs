using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Models;
using GIMI_ModManager.Core.GamesService.Requests;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services.CommandService;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Services.ModExport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

//using NativeFileDialogs.Net;

namespace GIMI_ModManager.WinUI.Views;

public sealed partial class DebugPage : Page
{
    public IGameService GameService = App.GetService<IGameService>();
    public CommandService CommandService = App.GetService<CommandService>();

    public IWindowManagerService WindowManagerService = App.GetService<IWindowManagerService>();

    public JsonExporterService JsonExporterService = App.GetService<JsonExporterService>();

    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();

    public DebugPage()
    {
        InitializeComponent();
    }


    private async void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        var createCharacterRequest = new CreateCharacterRequest()
        {
            DisplayName = "DebugTest",
            Element = "Pyro",
            Rarity = 5,
            InternalName = new InternalName("DebugTest"),
            IsMultiMod = false,
            ModFilesName = "DebugTest",
            Region = new[] { "Mondstadt" },
            Keys = new[] { "DebugTest", "Debugger" }
        };

        var pathPicker = new ViewModels.SubVms.PathPicker()
        {
            FileTypeFilter = [.. Constants.SupportedImageExtensions],
        };

        await pathPicker.BrowseFilePathAsync(App.MainWindow);
        if (string.IsNullOrEmpty(pathPicker.Path))
        {
            return;
        }

        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pathPicker.Path);
        if (file != null)
        {
            createCharacterRequest.Image = new Uri(file.Path);
        }

        var newCharacter = await GameService.CreateCharacterAsync(createCharacterRequest);

        await _skinManagerService.EnableModListAsync(newCharacter);
    }

    private async void ButtonBase_OnClick1(object sender, RoutedEventArgs e)
    {
        var character = GameService.GetCharacterByIdentifier("DebugTest");

        character ??= GameService.GetCharacterByIdentifier("NewName");

        var editCharacterRequest = new EditCustomCharacterRequest()
        {
            DisplayName = NewValue<string>.Set(character.DisplayName == "DebugTest" ? "NewName" : "DebugTest")
        };

        await GameService.EditCustomCharacterAsync(character.InternalName, editCharacterRequest);
    }

    private async void ButtonBase_OnClick2(object sender, RoutedEventArgs e)
    {
        var character = GameService.GetCharacterByIdentifier("DebugTest");

        character ??= GameService.GetCharacterByIdentifier("NewName");

        await GameService.DeleteCustomCharacterAsync(character.InternalName);
        await _skinManagerService.DisableModListAsync(character);
    }
}