using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkitWrapper;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities;
using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.Core.Entities.Mods.FileModels;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.Views.CharacterDetailsPages;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;

public sealed partial class ModPaneVM(
    ISkinManagerService skinManagerService,
    NotificationManager notificationService,
    ModSettingsService modSettingsService,
    ImageHandlerService imageHandlerService,
    IKeySwapService keySwapService)
    : ObservableRecipient, IRecipient<ModChangedMessage>
{
    private readonly ILogger _logger = Log.ForContext<ModPaneVM>();
    private readonly ISkinManagerService _skinManagerService = skinManagerService;
    private readonly NotificationManager _notificationService = notificationService;
    private readonly ModSettingsService _modSettingsService = modSettingsService;
    private readonly ImageHandlerService _imageHandlerService = imageHandlerService;

    private readonly AsyncLock _loadModLock = new();
    private CancellationToken _cancellationToken = CancellationToken.None;
    private DispatcherQueue _dispatcherQueue = null!;
    public bool IsInitialized { get; private set; }
    public BusySetter BusySetter { get; set; } = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotReadOnly))]
    private bool _isReadOnly = true;

    [ObservableProperty] private bool _isEditingModName;

    // 紧凑显示/编辑切换
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeySwapsModeText))]
    private bool _isKeySwapsEditMode;

    public bool IsNotReadOnly => !IsReadOnly;

    private Guid? _loadedModId;
    private CharacterSkinEntry? _loadedMod;

    [MemberNotNullWhen(true, nameof(_loadedModId), nameof(_loadedMod))]
    public bool IsModLoaded => _loadedModId != null && ModModel.IsLoaded && _loadedMod != null;

    [ObservableProperty] private ModPaneFieldsVm _modModel = new();

    // ini keyswaps 分组
    [ObservableProperty] private ObservableCollection<IniKeySwapGroupVm> _iniKeySwapGroups = [];

    // 控制是否显示 DISABLED 前缀的文件
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDisabledIniFilesText))]
    private bool _showDisabledIniFiles = false;

    public string ShowDisabledIniFilesText => ShowDisabledIniFiles ? "隐藏 DISABLED 文件" : "显示 DISABLED 文件";

    public bool QueueLoadMod(Guid? modId, bool force = false) => _channel.Writer.TryWrite(new LoadModMessage { ModId = modId, Force = force });

    private readonly Channel<LoadModMessage> _channel = Channel.CreateBounded<LoadModMessage>(
        new BoundedChannelOptions(1)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private async Task ModLoaderLoopAsync()
    {
        // Runs on the UI thread
        await foreach (var loadModMessage in _channel.Reader.ReadAllAsync(CancellationToken.None))
        {
            if (_cancellationToken.IsCancellationRequested)
                break;
            using var _ = await LockAsync().ConfigureAwait(false);
            IsReadOnly = true;
            IsEditingModName = false;
            try
            {
                if (loadModMessage.ModId is null)
                {
                    await UnloadModAsync();
                    NotifyAllCommands();
                    OnPropertyChanged(nameof(IsModLoaded));
                    continue;
                }

                await LoadModAsync(loadModMessage.ModId.Value, loadModMessage.Force);
                NotifyAllCommands();
                OnPropertyChanged(nameof(IsModLoaded));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _notificationService.ShowNotification("加载模组失败", e.Message, null);
            }
        }
    }

    private async Task LoadModAsync(Guid modId, bool force)
    {
        if (modId == _loadedModId && !force)
            return;

        var modPaneData = await Task.Run(async () =>
        {
            var modEntry = _skinManagerService.GetModEntryById(modId);
            if (modEntry == null)
                return null;

            var mod = modEntry.Mod;
            var modSettings = await mod.Settings.TryReadSettingsAsync(useCache: false, cancellationToken: _cancellationToken);
            if (modSettings is null)
                return null;

            var keySwapResult = await keySwapService.GetAllKeySwapsAsync(modId, ShowDisabledIniFiles);

            Dictionary<string, List<KeySwapSection>>? allKeySwaps = null;

            if (keySwapResult.IsSuccess)
            {
                allKeySwaps = keySwapResult.Value;

                if (!keySwapResult.HasNotification) return new { modEntry, modSettings, allKeySwaps };
                var notification = keySwapResult.Notification;
                _notificationService.ShowNotification(notification.Title, notification.Message, notification.Duration);
            }
            else
            {
                if (keySwapResult.HasNotification)
                {
                    var notification = keySwapResult.Notification;
                    _notificationService.ShowNotification(notification.Title, notification.Message, notification.Duration);
                }
                else if (keySwapResult.Exception != null)
                {
                    _notificationService.ShowNotification("错误", keySwapResult.Exception.Message, TimeSpan.FromSeconds(5));
                }

                allKeySwaps = [];
            }

            return new { modEntry, modSettings, allKeySwaps };
        }, _cancellationToken);

        if (modPaneData is null)
            return;

        _loadedMod = modPaneData.modEntry;
        ModModel = ModPaneFieldsVm.FromModEntry(modPaneData.modEntry, modPaneData.modSettings, []);
        ModModel.PropertyChanged += ModModel_PropertyChanged;
        _loadedModId = modId;
        IsReadOnly = false;
        IsKeySwapsEditMode = false; // 默认进入紧凑展示模式
        //填充 ini keyswaps 分组
        UpdateIniKeySwapGroups(modPaneData.allKeySwaps);
    }

    private void UpdateIniKeySwapGroups(Dictionary<string, List<KeySwapSection>>? allKeySwaps)
    {
        IniKeySwapGroups.Clear();
        IniKeySwapGroups.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasAnyKeySwap));

        if (allKeySwaps == null)
            return;

        foreach (var kv in allKeySwaps)
        {
            var group = new IniKeySwapGroupVm
            {
                IniFileName = kv.Key,
                ModPath = _loadedMod?.Mod.FullPath
            };

            foreach (var vm in kv.Value.Select(keySwap => new ModPaneFieldsKeySwapVm
            {
                ForwardHotkey = keySwap.ForwardKey,
                BackwardHotkey = keySwap.BackwardKey,
                SectionKey = keySwap.SectionName,
                Type = keySwap.Type,
                VariationsCount = keySwap.Variants?.ToString() ?? "0"
            }))
            {
                // 监听 SectionNameEditValid 变化
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ModPaneFieldsKeySwapVm.SectionNameEditValid))
                        OnPropertyChanged(nameof(CanSave));
                };

                vm.PropertyChanged += (_, e) =>
                {
                    OnPropertyChanged(nameof(ModModel));
                    if (e.PropertyName is nameof(ModPaneFieldsKeySwapVm.ForwardHotkey)
                        or nameof(ModPaneFieldsKeySwapVm.BackwardHotkey)
                        or nameof(ModPaneFieldsKeySwapVm.IsValid))
                    {
                        SaveModSettingsCommand.NotifyCanExecuteChanged();
                        OnPropertyChanged(nameof(CanSave));
                        NotifyAreAllKeySwapsValidChanged();
                    }
                };

                group.KeySwaps.Add(vm);
            }

            // 在 keyswap 加载/初始化时赋值 UnchangedValue
            foreach (var keySwap in group.KeySwaps)
            {
                keySwap.UnchangedValue = new ModPaneFieldsKeySwapVm
                {
                    SectionKey = keySwap.SectionKey,
                    ForwardHotkey = keySwap.ForwardHotkey,
                    BackwardHotkey = keySwap.BackwardHotkey,
                    Type = keySwap.Type,
                    VariationsCount = keySwap.VariationsCount
                };
            }

            IniKeySwapGroups.Add(group);
        }

        // 设置 UnchangedValue 用于变更检测
        foreach (var group in IniKeySwapGroups)
        {
            var unchangedGroup = new IniKeySwapGroupVm
            {
                IniFileName = group.IniFileName
            };

            foreach (var keySwap in group.KeySwaps)
            {
                unchangedGroup.KeySwaps.Add(new ModPaneFieldsKeySwapVm
                {
                    ForwardHotkey = keySwap.ForwardHotkey,
                    BackwardHotkey = keySwap.BackwardHotkey,
                    SectionKey = keySwap.SectionKey,
                    Type = keySwap.Type,
                    VariationsCount = keySwap.VariationsCount
                });
            }

            group.UnchangedValue = unchangedGroup;
        }
    }

    private void ModModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SaveModSettingsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSaveModSettings));
    }

    private void BusySetter_HardBusyChanged(object? sender, EventArgs eventArgs)
    {
        NotifyAllCommands();
        OnPropertyChanged(nameof(CanSaveModSettings));
    }

    private Task UnloadModAsync()
    {
        _loadedModId = null;
        _loadedMod = null;

        if (ModModel.IsLoaded)
            ModModel.PropertyChanged -= ModModel_PropertyChanged;
        IniKeySwapGroups.CollectionChanged -= (s, _) => OnPropertyChanged(nameof(HasAnyKeySwap));

        ModModel = new ModPaneFieldsVm();
        return Task.CompletedTask;
    }

    private readonly record struct LoadModMessage
    {
        public Guid? ModId { get; init; }
        public bool Force { get; init; }
    }

    public void Receive(ModChangedMessage message)
    {
        if (!IsModLoaded)
            return;

        if (message.SkinEntry.Id != _loadedModId)
            return;

        if (message.sender == this)
            return;

        QueueLoadMod(message.SkinEntry.Id, true);
    }

    public Task OnNavigatedToAsync(DispatcherQueue dispatcherQueue, CancellationToken navigationCt)
    {
        _dispatcherQueue = dispatcherQueue;
        _cancellationToken = navigationCt;
        _ = _dispatcherQueue.EnqueueAsync(ModLoaderLoopAsync);
        Messenger.RegisterAll(this);
        BusySetter.HardBusyChanged += BusySetter_HardBusyChanged;
        IsInitialized = true;
        return Task.CompletedTask;
    }

    public void OnNavigatedFrom()
    {
        _channel.Writer.TryComplete();
        Messenger.UnregisterAll(this);
        BusySetter.HardBusyChanged -= BusySetter_HardBusyChanged;

        if (ModModel.IsLoaded)
        {
            ModModel.PropertyChanged -= ModModel_PropertyChanged;
        }

        try
        {
            _loadModLock.Dispose();
        }
        catch (Exception e)
        {
            _logger.Warning(e, "Failed to dispose of load mod lock");
        }
    }

    private bool DefaultCanExecute => IsModLoaded && IsNotReadOnly && BusySetter.IsNotHardBusy;

    #region Commands

    private bool CanPickImageUri() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanPickImageUri))]
    private async Task PickImageUriAsync()
    {
        if (!IsModLoaded)
            return;

        try
        {
            var modFolderPath = _loadedMod.Mod.FullPath;

            var dataPackage = new DataPackage();
            dataPackage.SetText(modFolderPath);
            Clipboard.SetContent(dataPackage);

            _notificationService.ShowNotification("模组文件夹路径已复制到剪贴板", "", TimeSpan.FromSeconds(3));
        }
        catch (Exception e)
        {
            _logger.Error(e, "An error occured while trying to copy mod folder path to clipboard when picking image");
        }

        var filePicker = new FileOpenPicker
        {
            CommitButtonText = "设置图片",
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SettingsIdentifier = "ImagePicker"
        };

        foreach (var supportedImageExtension in Constants.SupportedImageExtensions)
            filePicker.FileTypeFilter.Add(supportedImageExtension);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);

        var file = await filePicker.PickSingleFileAsync();

        if (file == null)
            return;

        var imageUri = new Uri(file.Path);
        ModModel.ImageUri = imageUri;
    }

    private bool CanPasteImage() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanPasteImage))]
    private async Task PasteImageFromClipboardAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!IsModLoaded)
                return;

            var clipboardHasValidImageResult = await _imageHandlerService.ClipboardContainsImageAsync();

            if (!clipboardHasValidImageResult.Result)
            {
                _notificationService.ShowNotification("剪贴板中未包含有效的图像", "", null);
                return;
            }

            var imagePath = await _imageHandlerService.GetImageFromClipboardAsync(clipboardHasValidImageResult.DataPackage);

            if (imagePath == null)
            {
                _notificationService.ShowNotification("无法从剪贴板获取图片", "", null);
                return;
            }

            ModModel.ImageUri = imagePath;
        }).ConfigureAwait(false);
    }

    private bool CanCopyImageToClipboard() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanCopyImageToClipboard))]
    private async Task CopyImageToClipboardAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!File.Exists(ModModel.ImageUri.LocalPath))
                return;

            var file = await StorageFile.GetFileFromPathAsync(ModModel.ImageUri.LocalPath);
            if (file is null)
                return;

            await ImageHandlerService.CopyImageToClipboardAsync(file).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private bool CanClearImage() => DefaultCanExecute && ModModel.ImageUri != ImageHandlerService.StaticPlaceholderImageUri;

    [RelayCommand(CanExecute = nameof(CanClearImage))]
    private void ClearImage()
    {
        if (!IsModLoaded)
            return;

        ModModel.ImageUri = ImageHandlerService.StaticPlaceholderImageUri;
    }

    private bool HasKeySwapChanges()
    {
        return IniKeySwapGroups.Any(group => group.IsKeySwapsChanged);
    }

    public bool SectionNameEditValidAll
    {
        get
        {
            // 检查所有 keyswap 节点
            return IniKeySwapGroups.All(group =>
                group.KeySwaps.All(keySwap =>
                    keySwap.SectionNameEditValid));
        }
    }

    public bool CanSave => IsNotReadOnly;

    public bool AreAllKeySwapsValid
    {
        get
        {
            return IniKeySwapGroups.All(g =>
                g.KeySwaps.All(k =>
                    k is { IsValid: true, SectionNameEditValid: true }));
        }
    }

    private void NotifyAreAllKeySwapsValidChanged()
    {
        OnPropertyChanged(nameof(AreAllKeySwapsValid));
        NotifyCanSaveModSettingsChanged();
    }

    public bool CanSaveModSettings => DefaultCanExecute &&
                                      (ModModel.AnyChanges || HasKeySwapChanges()) &&
                                      AreAllKeySwapsValid;

    private void NotifyCanSaveModSettingsChanged()
    {
        OnPropertyChanged(nameof(CanSaveModSettings));
        SaveModSettingsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsReadOnlyChanged(bool value) => NotifyCanSaveModSettingsChanged();

    [RelayCommand(CanExecute = nameof(CanSaveModSettings))]
    private async Task SaveModSettingsAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!CanSaveModSettings)
                return;

            var existingModSettings = await _loadedMod!.Mod.Settings.ReadSettingsAsync();
            var updateRequest = new UpdateSettingsRequest();

            if (ModModel.IsImageUriChanged)
                updateRequest.SetImagePath = ModModel.ImageUri;
            if (ModModel.IsModDisplayNameChanged)
                updateRequest.SetCustomName = ModModel.ModDisplayName;
            if (ModModel.IsModUrlChanged)
                updateRequest.SetModUrl = Uri.TryCreate(ModModel.ModUrl, UriKind.Absolute, out var url) ? url : null;

            await Task.Run(async () =>
            {
                // 保存模组设置
                if (updateRequest.AnyUpdates)
                {
                    var settingsResult = await _modSettingsService.SaveSettingsAsync(_loadedModId.Value, updateRequest, _cancellationToken);

                    // 检查是否有通知需要显示
                    if (settingsResult.HasNotification)
                    {
                        var notification = settingsResult.Notification;
                        _notificationService.ShowNotification(notification.Title, notification.Message, notification.Duration);
                    }
                }

                // 保存所有修改的 keyswaps
                if (_loadedMod.Mod.KeySwaps is not null && IniKeySwapGroups.Count > 0 && HasKeySwapChanges())
                {
                    var updatedKeySwaps = new Dictionary<string, List<KeySwapSection>>();

                    foreach (var group in IniKeySwapGroups.Where(g => g.IsKeySwapsChanged))
                    {
                        var list = new List<KeySwapSection>();

                        foreach (var keySwap in group.KeySwaps.Where(k => k.IsKeySwapChanged()))
                        {
                            // 解析 ForwardHotkey 和 BackwardHotkey 到列表
                            var forwardKeys = new List<string>();
                            var backwardKeys = new List<string>();
                            if (!string.IsNullOrWhiteSpace(keySwap.ForwardHotkey))
                            {
                                forwardKeys.AddRange(ParseKeyString(keySwap.ForwardHotkey));
                            }

                            if (!string.IsNullOrWhiteSpace(keySwap.BackwardHotkey))
                            {
                                backwardKeys.AddRange(ParseKeyString(keySwap.BackwardHotkey));
                            }

                            list.Add(new KeySwapSection
                            {
                                SectionName = keySwap.SectionKey,
                                OriginalSectionName = keySwap.UnchangedValue?.SectionKey ?? keySwap.SectionKey,
                                ForwardKeys = forwardKeys,
                                BackwardKeys = backwardKeys
                            });
                        }

                        updatedKeySwaps[group.IniFileName] = list;
                    }

                    var keySwapResult = await keySwapService.SaveKeySwapsAsync(_loadedModId.Value, updatedKeySwaps);

                    if (keySwapResult.HasNotification)
                    {
                        var notification = keySwapResult.Notification;
                        _notificationService.ShowNotification(notification.Title, notification.Message, notification.Duration);
                    }
                }
            }, _cancellationToken);

            // 刷新视图
            Messenger.Send(new ModChangedMessage(this, _loadedMod, null));
            QueueLoadMod(_loadedModId, true);
        }).ConfigureAwait(false);
    }

    private bool CanOpenModFolder() => DefaultCanExecute;

    [RelayCommand(CanExecute = nameof(CanOpenModFolder))]
    private async Task OpenModFolderAsync()
    {
        await CommandWrapper(async () =>
        {
            if (!IsModLoaded)
                return;

            await Windows.System.Launcher.LaunchFolderAsync(
                await StorageFolder.GetFolderFromPathAsync(_loadedMod.Mod.FullPath));
        }).ConfigureAwait(false);
    }

    [RelayCommand]
    private void ToggleEditingModName()
    {
        IsEditingModName = !IsEditingModName;
    }

    public string KeySwapsModeText => IsKeySwapsEditMode ? "完成编辑" : "编辑键位";

    private bool CanToggleKeySwapsEditMode() => IsNotReadOnly;

    [RelayCommand(CanExecute = nameof(CanToggleKeySwapsEditMode))]
    private void ToggleKeySwapsEditMode()
    {
        IsKeySwapsEditMode = !IsKeySwapsEditMode;
    }

    [RelayCommand]
    private void ToggleShowDisabledIniFiles()
    {
        ShowDisabledIniFiles = !ShowDisabledIniFiles;
        if (IsModLoaded)
        {
            QueueLoadMod(_loadedModId, true);
        }
    }

    #endregion

    #region DragAndDropHandlers

    public bool CanSetImageFromDragDropWeb(Uri? url)
    {
        if (!DefaultCanExecute)
            return false;

        if (url is null || !url.IsAbsoluteUri)
            return false;

        if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            return false;

        return Constants.SupportedImageExtensions.Contains(Path.GetExtension(url.AbsolutePath));
    }

    public async Task SetImageFromDragDropWeb(Uri uri)
    {
        await CommandWrapper(async () =>
        {
            var image = await _imageHandlerService.DownloadImageAsync(uri, _cancellationToken);
            ModModel.ImageUri = new Uri(image.Path);
        }, true, useDefaultExceptionHandler: true).ConfigureAwait(false);
    }

    public bool CanSetImageFromDragDropStorageItem(IReadOnlyList<IStorageItem> storageItems)
    {
        if (!DefaultCanExecute)
            return false;

        if (storageItems.Count != 1)
            return false;

        var file = storageItems.First();

        if (!Uri.TryCreate(file.Path, UriKind.Absolute, out _))
            return false;

        return Constants.SupportedImageExtensions.Contains(Path.GetExtension(file.Name));
    }

    public async Task SetImageFromDragDropFile(IReadOnlyList<IStorageItem> storageItems)
    {
        await CommandWrapper(() =>
        {
            var file = storageItems.First();
            var filePath = new Uri(file.Path);
            ModModel.ImageUri = filePath;
            return Task.CompletedTask;
        }, true, useDefaultExceptionHandler: true).ConfigureAwait(false);
    }

    #endregion

    private async Task CommandWrapper(Func<Task> command, bool hardBusy = false, Action<Exception>? uncaughtErrorHandler = null,
        bool useDefaultExceptionHandler = false, [CallerMemberName] string commandName = "")
    {
        try
        {
            using var _ = await LockAsync().ConfigureAwait(false);
            using var busy = hardBusy ? BusySetter.StartHardBusy() : BusySetter.StartSoftBusy();

            await command();
        }
        catch (TaskCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (useDefaultExceptionHandler)
            {
                _logger.Error(e, "An error occured while executing command {CommandName}", commandName);
                _notificationService.ShowNotification($"An error occured running command {commandName}", e.Message, null);
                return;
            }

            if (uncaughtErrorHandler is null) throw;

            var ex = ExceptionDispatchInfo.Capture(e);
            uncaughtErrorHandler(ex.SourceException);
        }
    }

    private async Task CommandWrapper(Action command, bool hardBusy = false)
    {
        try
        {
            using var _ = await LockAsync().ConfigureAwait(false);
            using var busy = hardBusy ? BusySetter.StartHardBusy() : BusySetter.StartSoftBusy();
            command();
        }
        catch (TaskCanceledException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<IDisposable> LockAsync() =>
        await _loadModLock.LockAsync(cancellationToken: _cancellationToken).ConfigureAwait(false);

    private IRelayCommand[]? _viewModelCommands;

    private void NotifyAllCommands()
    {
        if (_viewModelCommands is null)
        {
            _viewModelCommands = GetType()
                .GetProperties()
                .Where(p => p.PropertyType.IsAssignableTo(typeof(IRelayCommand)))
                .Select(p => p.GetValue(this) as IRelayCommand)
                .Where(c => c != null)
                .ToArray()!;
        }

        foreach (var command in _viewModelCommands)
        {
            command.NotifyCanExecuteChanged();
        }
    }

    public bool HasAnyKeySwap => IniKeySwapGroups?.Any(g => g.KeySwaps.Count > 0) == true;

    private static List<string> ParseKeyString(string value)
    {
        var result = new List<string>();

        if (string.IsNullOrWhiteSpace(value))
            return result;

        var keyText = value.Trim();

        // 使用正则分割"作为分隔符的逗号"（仅前面是非逗号且无空格的逗号）
        var parts = VirtualKeyToFriendlyTextConverter.SeparatorCommaRegex.Split(keyText)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p)) // 过滤空项，但保留含逗号的有效部分
            .ToList();

        foreach (var part in parts)
        {
            // 检查是否是 "num X" 格式（如"num 0"到"num 9"）
            if (VirtualKeyToFriendlyTextConverter.IsNumFormat(part))
            {
                result.Add(part);
            }
            // 组合按键（含空格分隔，包括包含逗号key的组合）
            else if (part.Contains(' '))
            {
                // 仅分割空格并过滤空项，不再过滤NO_前缀
                var validSubParts = part.Split([' '], StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();

                if (validSubParts.Count > 0)
                {
                    // 重组有效组合键（保留空格分隔）
                    result.Add(string.Join(" ", validSubParts));
                }
            }
            // 单个按键（包括逗号key）
            else
            {
                result.Add(part);
            }
        }

        return result;
    }
}

public partial class ModPaneFieldsVm : ObservableObject
{
    public bool IsLoaded { get; private init; }
    public ModPaneFieldsVm? UnchangedValue { get; private init; }

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private Uri _imageUri = ImageHandlerService.StaticPlaceholderImageUri;
    public bool IsImageUriChanged => ImageUri != UnchangedValue?.ImageUri;
    [ObservableProperty] private string _modDisplayName = string.Empty;
    public bool IsModDisplayNameChanged => ModDisplayName != UnchangedValue?.ModDisplayName;
    [ObservableProperty] private string _modUrl = string.Empty;
    public bool IsModUrlChanged => ModUrl != UnchangedValue?.ModUrl;
    [ObservableProperty] private string? _modIniPath = null;
    public bool IsModIniPathChanged => ModIniPath != UnchangedValue?.ModIniPath;
    [ObservableProperty] private bool _ignoreMergedIni = true;
    public bool IsIgnoreMergedIniChanged => IgnoreMergedIni != UnchangedValue?.IgnoreMergedIni;

    public ObservableCollection<ModPaneFieldsKeySwapVm> KeySwaps { get; } = [];

    // ini keyswaps 分组
    public ObservableCollection<IniKeySwapGroupVm> IniKeySwapGroups { get; } = [];
    public bool IsIniKeySwapGroupsChanged => AnyIniKeySwapGroupsChanges();

    public string IsKeySwapManagementEnabled => (!IgnoreMergedIni).ToString().ToLower();

    private ModPaneFieldsVm(CharacterSkinEntry modEntry, ModSettings modSettings, IEnumerable<KeySwapSection> keySwaps)
    {
        IsEnabled = modEntry.IsEnabled;
        ImageUri = modSettings.ImagePath ?? ImageHandlerService.StaticPlaceholderImageUri;
        ModDisplayName = modEntry.Mod.GetDisplayName();
        ModUrl = modSettings.ModUrl?.ToString() ?? "";
        ModIniPath = modSettings.MergedIniPath?.ToString();
        IgnoreMergedIni = modSettings.IgnoreMergedIni;

        foreach (var keySwap in keySwaps)
        {
            KeySwaps.Add(new ModPaneFieldsKeySwapVm
            {
                ForwardHotkey = keySwap.ForwardKey,
                BackwardHotkey = keySwap.BackwardKey,
                SectionKey = keySwap.SectionName,
                Type = keySwap.Type,
                VariationsCount = keySwap.Variants?.ToString() ?? "无"
            });

            KeySwaps.Last().PropertyChanged += (_, e) => { OnPropertyChanged(nameof(KeySwaps)); };
        }

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AnyChanges))
                OnPropertyChanged(nameof(AnyChanges));
        };
    }

    public ModPaneFieldsVm()
    {
    }

    public static ModPaneFieldsVm FromModEntry(CharacterSkinEntry modEntry, ModSettings modSettings, ICollection<KeySwapSection> keySwaps)
    {
        var unchangedValue = new ModPaneFieldsVm(modEntry, modSettings, keySwaps)
        {
            IsLoaded = true,
            UnchangedValue = null
        };

        var result = new ModPaneFieldsVm(modEntry, modSettings, keySwaps)
        {
            IsLoaded = true,
            UnchangedValue = unchangedValue
        };

        return result;
    }

    public bool AnyChanges
    {
        get
        {
            if (UnchangedValue is null)
                return false;

            return IsEnabled != UnchangedValue.IsEnabled ||
                   ImageUri != UnchangedValue.ImageUri ||
                   ModDisplayName != UnchangedValue.ModDisplayName ||
                   ModUrl != UnchangedValue.ModUrl ||
                   ModIniPath != UnchangedValue.ModIniPath ||
                   IgnoreMergedIni != UnchangedValue.IgnoreMergedIni ||
                   IsIniKeySwapGroupsChanged;
        }
    }

    private bool AnyIniKeySwapGroupsChanges()
    {
        if (UnchangedValue is null)
            return false;

        if (IniKeySwapGroups.Count != UnchangedValue.IniKeySwapGroups.Count)
            return true;

        for (var i = 0; i < IniKeySwapGroups.Count; i++)
        {
            var oldGroup = UnchangedValue.IniKeySwapGroups[i];
            var newGroup = IniKeySwapGroups[i];

            if ((oldGroup.IniFileName ?? "") != (newGroup.IniFileName ?? ""))
                return true;

            if (oldGroup.KeySwaps.Count != newGroup.KeySwaps.Count)
                return true;

            for (var j = 0; j < newGroup.KeySwaps.Count; j++)
            {
                var oldKeySwap = oldGroup.KeySwaps[j];
                var newKeySwap = newGroup.KeySwaps[j];

                if ((oldKeySwap.ForwardHotkey ?? "") != (newKeySwap.ForwardHotkey ?? "") ||
                    (oldKeySwap.BackwardHotkey ?? "") != (newKeySwap.BackwardHotkey ?? ""))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

// ini keyswap 分组 ViewModel
public partial class IniKeySwapGroupVm
    : ObservableObject
{
    [ObservableProperty] private string _iniFileName = string.Empty;

    public ObservableCollection<ModPaneFieldsKeySwapVm> KeySwaps { get; } = [];

    // 用于变更检测的原始值
    public IniKeySwapGroupVm? UnchangedValue { get; set; }

    // 模组路径属性
    public string? ModPath { get; set; }

    // 打开ini文件的命令
    [RelayCommand]
    private async Task OpenIniFileAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(IniFileName) || string.IsNullOrWhiteSpace(ModPath))
                return;

            var fullPath = Path.Combine(ModPath, IniFileName);
            if (!File.Exists(fullPath))
                return;

            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = fullPath;
            process.StartInfo.UseShellExecute = true;
            process.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open ini file: {ex.Message}");
        }
    }

    public bool IsKeySwapsChanged
    {
        get
        {
            if (UnchangedValue is null)
                return false;

            if (KeySwaps.Count != UnchangedValue.KeySwaps.Count)
                return true;

            return KeySwaps.Zip(UnchangedValue.KeySwaps, (newKs, oldKs) =>
                    (newKs.ForwardHotkey ?? "") != (oldKs.ForwardHotkey ?? "") ||
                    (newKs.BackwardHotkey ?? "") != (oldKs.BackwardHotkey ?? "") ||
                    (newKs.SectionKey ?? "") != (oldKs.SectionKey ?? ""))
                .Any(changed => changed);
        }
    }
}

public partial class ModPaneFieldsKeySwapVm : ObservableObject
{
    [ObservableProperty] private string _sectionKey = string.Empty;
    [ObservableProperty] private string? _condition;
    [ObservableProperty] private string? _forwardHotkey;
    [ObservableProperty] private string? _backwardHotkey;
    [ObservableProperty] private string? _type;
    [ObservableProperty] private string _variationsCount = "0";

    private string? _sectionNamePrefix;

    // 节名称编辑相关
    [ObservableProperty] private bool _isEditingSectionName = false;

    // 用于保存原始快照
    public ModPaneFieldsKeySwapVm? UnchangedValue { get; set; }

    // 添加友好文本属性
    [ObservableProperty] private string? _forwardHotkeyFriendlyText;

    [ObservableProperty] private string? _backwardHotkeyFriendlyText;

    [RelayCommand]
    private void ToggleEditingSectionName()
    {
        IsEditingSectionName = !IsEditingSectionName;
    }

    // 编辑用属性：去掉前缀和方括号
    public bool SectionNameEditValid => !string.IsNullOrWhiteSpace(SectionNameForEdit?.Trim()) &&
                                        !SectionNameForEdit.Contains(' ');

    public bool ForwardHotkeyValid => !string.IsNullOrWhiteSpace(ForwardHotkey?.Trim());
    public bool BackwardHotkeyValid => !string.IsNullOrWhiteSpace(BackwardHotkey?.Trim());

    public bool IsValid => SectionNameEditValid && ForwardHotkeyValid;

    public string DisplaySectionName
    {
        get
        {
            var sectionName = SectionKey;
            var content = sectionName.TrimStart('[').TrimEnd(']');
            var displayContent = RemoveKeyPrefixes(content);
            return $"[{displayContent}]";
        }
    }

    public string SectionNameForEdit
    {
        get
        {
            var sectionName = SectionKey.TrimStart('[').TrimEnd(']');
            return RemoveKeyPrefixes(sectionName);
        }
        set
        {
            var trimmedValue = value?.Replace(" ", "") ?? string.Empty;
            var sectionName = SectionKey.TrimStart('[').TrimEnd(']');

            if (_sectionNamePrefix == null)
            {
                _sectionNamePrefix = "";
                if (sectionName.StartsWith(IniKeySwapSection.KeySwapIniSection, StringComparison.OrdinalIgnoreCase))
                    _sectionNamePrefix = sectionName[..IniKeySwapSection.KeySwapIniSection.Length];
                else if (sectionName.StartsWith(IniKeySwapSection.CommandListSection, StringComparison.OrdinalIgnoreCase))
                    _sectionNamePrefix = sectionName[..IniKeySwapSection.CommandListSection.Length];
                else if (sectionName.StartsWith(IniKeySwapSection.ForwardIniKey, StringComparison.OrdinalIgnoreCase))
                    _sectionNamePrefix = sectionName[..IniKeySwapSection.ForwardIniKey.Length];
            }

            var newSectionKey = string.IsNullOrWhiteSpace(trimmedValue) ? "[]" : $"[{_sectionNamePrefix}{trimmedValue}]";
            if (SectionKey != newSectionKey)
            {
                SectionKey = newSectionKey;
                OnPropertyChanged(nameof(SectionKey));
                OnPropertyChanged(nameof(DisplaySectionName));
                OnPropertyChanged(nameof(SectionNameForEdit));
                OnPropertyChanged(nameof(SectionNameEditValid));
                OnPropertyChanged(nameof(IsValid));
            }
        }
    }

    public Visibility VariationsCountVisibility =>
        Type?.Equals("cycle", StringComparison.OrdinalIgnoreCase) == true ? Visibility.Visible : Visibility.Collapsed;

    private static string RemoveKeyPrefixes(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        var displayName = name;

        // 移除KeySwap前缀（不区分大小写）
        if (displayName.StartsWith(IniKeySwapSection.KeySwapIniSection, StringComparison.OrdinalIgnoreCase) &&
            displayName.Length > IniKeySwapSection.KeySwapIniSection.Length)
        {
            displayName = displayName[IniKeySwapSection.KeySwapIniSection.Length..];
        }
        else if (displayName.StartsWith(IniKeySwapSection.CommandListSection, StringComparison.OrdinalIgnoreCase) &&
                 displayName.Length > IniKeySwapSection.CommandListSection.Length)
        {
            displayName = displayName[IniKeySwapSection.CommandListSection.Length..];
        }
        else if (displayName.StartsWith(IniKeySwapSection.ForwardIniKey, StringComparison.OrdinalIgnoreCase))
        {
            displayName = displayName[IniKeySwapSection.ForwardIniKey.Length..];
        }

        return displayName;
    }

    public bool IsKeySwapChanged()
    {
        if (UnchangedValue is null)
            return false;

        return (ForwardHotkey ?? "") != (UnchangedValue.ForwardHotkey ?? "") ||
               (BackwardHotkey ?? "") != (UnchangedValue.BackwardHotkey ?? "") ||
               (SectionKey ?? "") != (UnchangedValue.SectionKey ?? "");
    }

    partial void OnForwardHotkeyChanged(string? value)
    {
        ForwardHotkeyFriendlyText = ConvertToFriendlyText(value);
        OnPropertyChanged(nameof(ForwardHotkeyValid));
        OnPropertyChanged(nameof(IsValid));
    }

    partial void OnBackwardHotkeyChanged(string? value)
    {
        BackwardHotkeyFriendlyText = ConvertToFriendlyText(value);
    }

    private static string? ConvertToFriendlyText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : VirtualKeyToFriendlyTextConverter.ConvertToFriendlyText(value);
    }
}