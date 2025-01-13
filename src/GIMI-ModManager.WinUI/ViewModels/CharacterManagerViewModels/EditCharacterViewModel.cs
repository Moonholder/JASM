﻿using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.GamesService.Models;
using GIMI_ModManager.Core.GamesService.Requests;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Contracts.ViewModels;
using GIMI_ModManager.WinUI.Helpers;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.ViewModels.CharacterManagerViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Newtonsoft.Json;
using Serilog;

namespace GIMI_ModManager.WinUI.ViewModels;

public partial class EditCharacterViewModel : ObservableRecipient, INavigationAware
{
    private readonly IGameService _gameService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly ILogger _logger;
    private readonly NotificationManager _notificationManager;
    private readonly ImageHandlerService _imageHandlerService;
    private readonly INavigationService _navigationService;

    private ICharacter _character = null!;


    [ObservableProperty] private Uri _modFolderUri = null!;
    [ObservableProperty] private string _modFolderString = "";
    [ObservableProperty] private string _modsCount;
    [ObservableProperty] private string _keyToAddInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotCustomCharacter))]
    private bool _isCustomCharacter;

    public bool IsNotCustomCharacter => !IsCustomCharacter;

    public CharacterStatus CharacterStatus { get; } = new();

    public EditCharacterForm Form { get; } = new();

    public ObservableCollection<ModModel> Mods { get; } = new();


    public EditCharacterViewModel(IGameService gameService, ILogger logger, ISkinManagerService skinManagerService,
        ImageHandlerService imageHandlerService, NotificationManager notificationManager, INavigationService navigationService)
    {
        _gameService = gameService;
        _logger = logger.ForContext<EditCharacterViewModel>();
        _skinManagerService = skinManagerService;
        _imageHandlerService = imageHandlerService;
        _notificationManager = notificationManager;
        _navigationService = navigationService;
        Form.PropertyChanged += NotifyAllCommands;
    }


    public void OnNavigatedTo(object parameter)
    {
        if (parameter is not string internalName)
        {
            _logger.Error($"Invalid parameter type, {parameter}");
            internalName = _gameService.GetCharacters().First().InternalName;
        }

        if (parameter is InternalName id)
            internalName = id;


        var character = _gameService.GetCharacterByIdentifier(internalName, true);

        if (character is null)
        {
            _logger.Error($"Invalid character identifier, {internalName}");
            character = _gameService.GetCharacters().First();
        }

        _character = character;
        IsCustomCharacter = _character.IsCustomModObject;
        CharacterStatus.SetIsCustomCharacter(_character.IsCustomModObject);

        if (!_gameService.GetDisabledCharacters().Contains(character))
        {
            CharacterStatus.SetEnabled(true);
            var modList = _skinManagerService.GetCharacterModList(_character);
            ModFolderUri = new Uri(modList.AbsModsFolderPath);
            ModFolderString = ModFolderUri.LocalPath;
            ModsCount = modList.Mods.Count.ToString();
            Mods.AddRange(modList.Mods.Select(m => ModModel.FromMod(m.Mod)));
        }
        else
        {
            CharacterStatus.SetEnabled(false);
            ModFolderUri = new Uri(_skinManagerService.ActiveModsFolderPath);
            ModFolderString = ModFolderUri.LocalPath;
            ModsCount = "-";
        }


        var allModObjects = _gameService.GetAllModdableObjects(GetOnly.Both);

        Form.Initialize(character, allModObjects);
        NotifyAllCommands();
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand]
    private void AddKey()
    {
        KeyToAddInput = KeyToAddInput.Trim().ToLowerInvariant();
        var existingKeys = Form.Keys.Items.ToList();

        if (string.IsNullOrWhiteSpace(KeyToAddInput))
            return;

        if (existingKeys.Contains(KeyToAddInput, StringComparer.OrdinalIgnoreCase))
            return;

        Form.Keys.Items.Add(KeyToAddInput);
        KeyToAddInput = string.Empty;
        NotifyAllCommands();
    }

    [RelayCommand]
    private void RemoveKey(string key)
    {
        Form.Keys.Items.Remove(key);
        NotifyAllCommands();
    }


    #region ImageCommands

    [RelayCommand]
    private async Task PickImage()
    {
        var image = await _imageHandlerService.PickImageAsync(copyToTmpFolder: false);

        if (image is null || !File.Exists(image.Path))
            return;

        Form.Image.Value = new Uri(image.Path);
    }

    [RelayCommand]
    private async Task PasteImageAsync()
    {
        try
        {
            var image = await _imageHandlerService.GetImageFromClipboardAsync();
            if (image is not null)
                Form.Image.Value = image;
            else
                _notificationManager.ShowNotification("粘贴图片失败", "在剪贴板中未找到图片", null);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to paste image");
            _notificationManager.ShowNotification("F粘贴图片失败", ex.Message, null);
        }
    }

    [RelayCommand]
    private void ClearImage() => Form.Image.Value = _character.DefaultCharacter.ImageUri ?? ImageHandlerService.StaticPlaceholderImageUri;

    [RelayCommand]
    private async Task SelectImageAsync()
    {
        var image = await _imageHandlerService.PickImageAsync(copyToTmpFolder: false);
        if (image is not null && Uri.TryCreate(image.Path, UriKind.Absolute, out var imagePath))
            Form.Image.Value = imagePath;
    }

    #endregion

    private bool CanDisableCharacter() => !_character.IsCustomModObject && CharacterStatus.IsEnabled && !AnyChanges();

    [RelayCommand(CanExecute = nameof(CanDisableCharacter))]
    private async Task DisableCharacter()
    {
        var deleteFolderCheckBox = new CheckBox()
        {
            Content = "删除角色文件夹及其内容 / 模组?\n" +
                      "文件将被永久删除!",
            IsChecked = false
        };

        var dialogContent = new StackPanel()
        {
            Children =
            {
                new TextBlock()
                {
                    Text =
                        "你确定要禁用这个角色吗？ " +
                        "这不会移除该角色，但 JASM将不再识别这个角色。 " +
                        "该角色稍后可以重新激活。 " +
                        "按下 “是” 后，这将立即执行。",
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Margin = new Thickness(0, 0, 0, 8)
                },
                deleteFolderCheckBox
            }
        };


        var disableDialog = new ContentDialog
        {
            Title = "禁用角色",
            Content = dialogContent,
            PrimaryButtonText = "是,禁用这个角色",
            CloseButtonText = "不,取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var result = await disableDialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
            return;

        _logger.Information(
            $"正在禁用内部名称为 {_character.InternalName} 的角色，删除文件夹选项={deleteFolderCheckBox.IsChecked}");
        var deleteFolder = deleteFolderCheckBox.IsChecked ?? false;
        await Task.Run(async () =>
        {
            await _gameService.DisableCharacterAsync(_character).ConfigureAwait(false);
            await _skinManagerService.DisableModListAsync(_character, deleteFolder).ConfigureAwait(false);
        });
        ResetState();
    }


    private bool CanDeleteCustomCharacter() => _character.IsCustomModObject && CharacterStatus.IsEnabled && !Form.AnyFieldDirty;

    [RelayCommand(CanExecute = nameof(CanDeleteCustomCharacter))]
    private async Task DeleteCustomCharacterAsync()
    {
        var deleteFolderCheckBox = new CheckBox()
        {
            Content = "是否删除自定义角色文件夹及其内容 / 模组？" +
                      "文件将会被永久删除！",
            IsChecked = false
        };

        var dialogContent = new StackPanel()
        {
            Children =
            {
                new TextBlock()
                {
                    Text =
                        "你确定要删除这个自定义角色吗？" +
                        "这将会移除该角色，而且 JASM将不再识别这个角色。" +
                        "“按下‘是’后，这将立即执行。",
                    TextWrapping = TextWrapping.WrapWholeWords,
                    Margin = new Thickness(0, 0, 0, 8)
                },
                deleteFolderCheckBox
            }
        };


        var disableDialog = new ContentDialog
        {
            Title = "删除自定义角色",
            Content = dialogContent,
            PrimaryButtonText = "是,删除这个角色",
            CloseButtonText = "不，取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var result = await disableDialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
            return;


        _logger.Information($"正在删除自定义角色 {_character.InternalName}");
        var deleteFolder = deleteFolderCheckBox.IsChecked ?? false;
        await Task.Run(async () =>
        {
            await _gameService.DeleteCustomCharacterAsync(_character.InternalName).ConfigureAwait(false);
            await _skinManagerService.DisableModListAsync(_character, deleteFolder).ConfigureAwait(false);
        });

        _navigationService.NavigateTo(typeof(CharacterManagerViewModel).FullName!, new object(), clearNavigation: true);
        _notificationManager.ShowNotification("自定义角色已删除", $"自定义角色 '{_character.DisplayName}' 已成功删除", null);
    }

    private bool CanEnableCharacter() => !_character.IsCustomModObject && CharacterStatus.IsDisabled && !AnyChanges();

    [RelayCommand(CanExecute = nameof(CanEnableCharacter))]
    private async Task EnableCharacter()
    {
        var dialogContent = new TextBlock()
        {
            Text =
                "你确定要启用这个角色吗？" +
                "按下‘是’后，这将立即执行。",
            TextWrapping = TextWrapping.WrapWholeWords,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var enableDialog = new ContentDialog
        {
            Title = "启用角色",
            Content = dialogContent,
            PrimaryButtonText = "是,启用这个角色",
            CloseButtonText = "不,取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var result = await enableDialog.ShowAsync();

        if (result != ContentDialogResult.Primary)
            return;

        _logger.Information($"Enabling character {_character.InternalName}");

        await _gameService.EnableCharacterAsync(_character);
        await _skinManagerService.EnableModListAsync(_character);
        ResetState();
    }

    [RelayCommand]
    private void GoToCharacter()
    {
        _navigationService.NavigateToCharacterDetails(_character.InternalName);
    }

    private bool CanSaveChanges() => Form.AnyFieldDirty && Form.IsValid;

    [RelayCommand(CanExecute = nameof(CanSaveChanges))]
    private void DummySave()
    {
    }

    [RelayCommand(CanExecute = nameof(CanSaveChanges))]
    private async Task SaveChangesAsync()
    {
        _logger.Debug($"Saving changes to character {_character.InternalName}");
        Form.ValidateAllFields();

        if (!AnyChanges() || !Form.IsValid)
            return;

        try
        {
            await InternalSaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to save changes to character");
            _notificationManager.ShowNotification("未能保存对角色所做的更改", e.Message, null);
            return;
        }


        ResetState();

        return;

        async Task InternalSaveChangesAsync()
        {
            if (_character.IsCustomModObject)
            {
                var editRequest = new EditCustomCharacterRequest();

                if (Form.DisplayName.IsDirty)
                {
                    Debug.Assert(Form.DisplayName.IsValid);
                    editRequest.DisplayName = NewValue<string>.Set(Form.DisplayName.Value.Trim());
                }

                if (Form.IsMultiMod.IsDirty)
                {
                    Debug.Assert(Form.IsMultiMod.IsValid);
                    editRequest.IsMultiMod = NewValue<bool>.Set(Form.IsMultiMod.Value);
                }

                if (Form.Image.IsDirty)
                {
                    Debug.Assert(Form.Image.IsValid);
                    var image = Form.Image.Value == ImageHandlerService.StaticPlaceholderImageUri ? null : Form.Image.Value;
                    editRequest.Image = NewValue<Uri?>.Set(image);
                }

                if (Form.Keys.IsDirty)
                {
                    Debug.Assert(Form.Keys.IsValid);
                    editRequest.Keys = NewValue<string[]>.Set(Form.Keys.Items.ToArray());
                }


                Debug.Assert(editRequest.AnyValuesSet);
                await Task.Run(() => _gameService.EditCustomCharacterAsync(_character.InternalName, editRequest)).ConfigureAwait(false);
            }
            else
            {
                var overrideRequest = new OverrideCharacterRequest();

                if (Form.DisplayName.IsDirty)
                {
                    Debug.Assert(Form.DisplayName.IsValid);
                    overrideRequest.DisplayName = NewValue<string>.Set(Form.DisplayName.Value.Trim());
                }

                if (Form.IsMultiMod.IsDirty)
                {
                    Debug.Assert(Form.IsMultiMod.IsValid);
                    overrideRequest.IsMultiMod = NewValue<bool>.Set(Form.IsMultiMod.Value);
                }

                if (Form.Image.IsDirty)
                {
                    Debug.Assert(Form.Image.IsValid);
                    var image = Form.Image.Value == ImageHandlerService.StaticPlaceholderImageUri
                                || Form.Image.Value == _character.DefaultCharacter.ImageUri
                        ? null
                        : Form.Image.Value;
                    overrideRequest.Image = NewValue<Uri?>.Set(image);
                }

                if (Form.Keys.IsDirty)
                {
                    Debug.Assert(Form.Keys.IsValid);
                    overrideRequest.Keys = NewValue<string[]>.Set(Form.Keys.Items.ToArray());
                }

                Debug.Assert(overrideRequest.AnyValuesSet);
                await Task.Run(() => _gameService.SetCharacterOverrideAsync(_character, overrideRequest)).ConfigureAwait(false);
            }
        }
    }


    private bool CanRevertChanges() => AnyChanges();

    [RelayCommand(CanExecute = nameof(CanRevertChanges))]
    private void RevertChanges()
    {
        _logger.Debug($"Reverting draft changes to character {_character.InternalName}");
        ResetState();
    }

    private bool CanResetCharacterToDefault() => !_character.IsCustomModObject;

    [RelayCommand(CanExecute = nameof(CanResetCharacterToDefault))]
    private async Task ResetCharacterToDefaultAsync()
    {
        _logger.Information($"Resetting character {_character.InternalName} to default");
        await _gameService.ResetOverrideForCharacterAsync(_character);
        ResetState();
    }

    private void ResetState()
    {
        Mods.Clear();
        OnNavigatedTo(_character.InternalName.Id);
        NotifyAllCommands();
    }

    private bool AnyChanges() => Form.AnyFieldDirty;

    private void NotifyAllCommands(object? sender = null, PropertyChangedEventArgs? propertyChangedEventArgs = null)
    {
        DummySaveCommand.NotifyCanExecuteChanged();
        DisableCharacterCommand.NotifyCanExecuteChanged();
        EnableCharacterCommand.NotifyCanExecuteChanged();
        SaveChangesCommand.NotifyCanExecuteChanged();
        RevertChangesCommand.NotifyCanExecuteChanged();
        ResetCharacterToDefaultCommand.NotifyCanExecuteChanged();
        DeleteCustomCharacterCommand.NotifyCanExecuteChanged();
    }


    [RelayCommand]
    private async Task ShowCharacterModelAsync()
    {
        var json = JsonConvert.SerializeObject(_character, Formatting.Indented);


        var content = new ScrollViewer()
        {
            Content = new TextBlock()
            {
                Text = json,
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true,
                Margin = new Thickness(4)
            }
        };


        var dialogHeight = App.MainWindow.Height * 0.5;
        var dialogWidth = App.MainWindow.Width * 0.7;
        var contentWrapper = new Grid()
        {
            MinHeight = dialogHeight,
            MinWidth = dialogWidth,
            Children =
            {
                content
            }
        };

        var characterModelDialog = new ContentDialog
        {
            Title = "Character Model",
            Content = contentWrapper,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = App.MainWindow.Content.XamlRoot,
            Resources =
            {
                ["ContentDialogMaxWidth"] = 8000,
                ["ContentDialogMaxHeight"] = 4000
            }
        };

        await characterModelDialog.ShowAsync();
    }
}

public class CharacterStatus : ObservableObject
{
    private bool _isCustomCharacter;
    public bool IsCustomCharacter => _isCustomCharacter;
    private bool _isEnabled;

    public bool IsEnabled => _isEnabled;

    private bool _isDisabled;

    public bool IsDisabled => _isDisabled;

    public bool IsEnabledAndNotCustomCharacter => IsEnabled && !IsCustomCharacter;

    public bool IsDisabledAndNotCustomCharacter => IsDisabled && !IsCustomCharacter;


    public void SetIsCustomCharacter(bool isCustomCharacter)
    {
        SetProperty(ref _isCustomCharacter, isCustomCharacter, nameof(IsCustomCharacter));
        OnPropertyChanged(nameof(IsDisabledAndNotCustomCharacter));
        OnPropertyChanged(nameof(IsEnabledAndNotCustomCharacter));
    }

    public void SetEnabled(bool enabled)
    {
        SetProperty(ref _isEnabled, enabled, nameof(IsEnabled));
        SetProperty(ref _isDisabled, !enabled, nameof(IsDisabled));
        OnPropertyChanged(nameof(IsDisabledAndNotCustomCharacter));
        OnPropertyChanged(nameof(IsEnabledAndNotCustomCharacter));
    }
}