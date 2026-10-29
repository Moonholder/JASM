using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkitWrapper;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities;
using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.Core.Entities.Mods.Helpers;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.ViewModels.SubVms;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;
using Serilog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;

public sealed partial class ModPaneVM(
    ISkinManagerService skinManagerService,
    NotificationManager notificationService,
    ModSettingsService modSettingsService,
    ImageHandlerService imageHandlerService,
    IKeySwapService keySwapService,
    ILanguageLocalizer localizer)
    : ObservableRecipient, IRecipient<ModChangedMessage>
{
    private readonly ILogger _logger = Log.ForContext<ModPaneVM>();
    private readonly ISkinManagerService _skinManagerService = skinManagerService;
    private readonly NotificationManager _notificationService = notificationService;
    private readonly ModSettingsService _modSettingsService = modSettingsService;
    private readonly ImageHandlerService _imageHandlerService = imageHandlerService;
    private readonly ILanguageLocalizer _localizer = localizer;

    private readonly AsyncLock _loadModLock = new();
    private CancellationToken _cancellationToken = CancellationToken.None;
    private DispatcherQueue _dispatcherQueue = null!;
    public bool IsInitialized { get; private set; }
    public BusySetter BusySetter { get; set; } = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotReadOnly))]
    private bool _isReadOnly = true;

    [ObservableProperty] private bool _isEditingModName;

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

    public string ShowDisabledIniFilesText => ShowDisabledIniFiles
        ? _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/HideDisabledIniFiles", "Hide DISABLED files")
        : _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/ShowDisabledIniFiles", "Show DISABLED files");

    private long _latestRequestedSequence;

    public bool QueueLoadMod(Guid? modId, bool force = false)
    {
        var sequence = Interlocked.Increment(ref _latestRequestedSequence);
        return _channel.Writer.TryWrite(new LoadModMessage { ModId = modId, Force = force, Sequence = sequence });
    }

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
        await foreach (var loadModMessage in _channel.Reader.ReadAllAsync(CancellationToken.None))
        {
            if (_cancellationToken.IsCancellationRequested)
                break;

            if (loadModMessage.Sequence < Volatile.Read(ref _latestRequestedSequence))
                continue;

            try
            {
                using var _ = await LockAsync().ConfigureAwait(false);

                if (loadModMessage.Sequence < Volatile.Read(ref _latestRequestedSequence))
                    continue;

                await _dispatcherQueue.EnqueueAsync(() =>
                {
                    if (loadModMessage.Sequence < Volatile.Read(ref _latestRequestedSequence))
                        return;

                    IsReadOnly = true;
                    IsEditingModName = false;
                });

                if (loadModMessage.ModId is null)
                {
                    await _dispatcherQueue.EnqueueAsync(() =>
                    {
                        if (loadModMessage.Sequence < Volatile.Read(ref _latestRequestedSequence))
                            return;

                        UnloadMod();
                        NotifyAllCommands();
                        OnPropertyChanged(nameof(IsModLoaded));
                    });
                    continue;
                }

                await LoadModAsync(loadModMessage.ModId.Value, loadModMessage.Force, loadModMessage.Sequence);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error loading mod in ModLoaderLoopAsync");
                _dispatcherQueue.TryEnqueue(() =>
                {
                    _notificationService.ShowNotification("加载模组失败", e.Message, null);
                });
            }
            finally
            {
                if (loadModMessage.Sequence >= Volatile.Read(ref _latestRequestedSequence))
                {
                    await _dispatcherQueue.EnqueueAsync(() =>
                    {
                        if (loadModMessage.Sequence < Volatile.Read(ref _latestRequestedSequence))
                            return;

                        IsReadOnly = false;
                        NotifyAllCommands();
                        OnPropertyChanged(nameof(IsModLoaded));
                    });
                }
            }
        }
    }

    private async Task LoadModAsync(Guid modId, bool force, long sequence)
    {
        if (modId == _loadedModId && !force)
            return;

        var modPaneData = await Task.Run(async () =>
        {
            if (sequence < Volatile.Read(ref _latestRequestedSequence))
                return null;

            var modEntry = _skinManagerService.GetModEntryById(modId);
            if (modEntry == null)
                return null;

            var mod = modEntry.Mod;
            var modSettings = await mod.Settings.TryReadSettingsAsync(useCache: false, cancellationToken: _cancellationToken);

            if (sequence < Volatile.Read(ref _latestRequestedSequence))
                return null;

            if (modSettings is null)
            {
                await Task.Delay(60, _cancellationToken).ConfigureAwait(false);
                if (sequence < Volatile.Read(ref _latestRequestedSequence))
                    return null;
                modSettings = await mod.Settings.TryReadSettingsAsync(useCache: false, cancellationToken: _cancellationToken);
            }

            if (modSettings is null)
            {
                var previewImages = SkinModHelpers.DetectModPreviewImages(mod.FullPath);
                modSettings = new ModSettings(
                    modEntry.Id,
                    customName: mod.GetDisplayName(),
                    imagePath: previewImages.FirstOrDefault()
                );
            }

            if (sequence < Volatile.Read(ref _latestRequestedSequence))
                return null;

            var keySwapResult = await keySwapService.GetAllKeySwapsAsync(modId, ShowDisabledIniFiles);

            if (sequence < Volatile.Read(ref _latestRequestedSequence))
                return null;

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

        if (sequence < Volatile.Read(ref _latestRequestedSequence))
            return;

        if (modPaneData is null)
        {
            await _dispatcherQueue.EnqueueAsync(() =>
            {
                if (sequence < Volatile.Read(ref _latestRequestedSequence))
                    return;

                UnloadMod();
                NotifyAllCommands();
                OnPropertyChanged(nameof(IsModLoaded));
            });
            return;
        }

        await _dispatcherQueue.EnqueueAsync(() =>
        {
            if (sequence < Volatile.Read(ref _latestRequestedSequence))
                return;

            if (ModModel.IsLoaded)
            {
                ModModel.PropertyChanged -= ModModel_PropertyChanged;
            }

            _loadedMod = modPaneData.modEntry;
            ModModel = ModPaneFieldsVm.FromModEntry(modPaneData.modEntry, modPaneData.modSettings, []);
            ModModel.PropertyChanged += ModModel_PropertyChanged;
            _loadedModId = modId;
            IsReadOnly = false;

            UpdateIniKeySwapGroups(modPaneData.allKeySwaps);
            NotifyAllCommands();
            OnPropertyChanged(nameof(ModModel));
            OnPropertyChanged(nameof(IsModLoaded));
        });
    }

    private void UpdateIniKeySwapGroups(Dictionary<string, List<KeySwapSection>>? allKeySwaps)
    {
        var expandedStates = IniKeySwapGroups.ToDictionary(g => g.IniFileName, g => g.IsExpanded);

        var newGroups = new List<IniKeySwapGroupVm>();

        if (allKeySwaps != null)
        {
            foreach (var kv in allKeySwaps)
            {
                var group = new IniKeySwapGroupVm
                {
                    IniFileName = kv.Key,
                    ModPath = _loadedMod?.Mod.FullPath,
                    IsExpanded = expandedStates.TryGetValue(kv.Key, out var wasExpanded) ? wasExpanded : true
                };

                foreach (var keySwap in kv.Value)
                {
                    var vm = new ModPaneFieldsKeySwapVm
                    {
                        SectionKey = keySwap.SectionName,
                        Type = keySwap.Type,
                        VariationsCount = keySwap.Variants?.ToString() ?? "0"
                    };

                    var forwardKeys = keySwap.ForwardKeys.Count > 0
                        ? keySwap.ForwardKeys
                        : KeyMappingHelpers.SplitMultiKeys(keySwap.ForwardKey);
                    var backwardKeys = keySwap.BackwardKeys.Count > 0
                        ? keySwap.BackwardKeys
                        : KeyMappingHelpers.SplitMultiKeys(keySwap.BackwardKey);

                    vm.LoadKeys(forwardKeys, backwardKeys);

                    vm.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(ModPaneFieldsKeySwapVm.SectionNameEditValid))
                        {
                            OnPropertyChanged(nameof(CanSave));
                            NotifyAreAllKeySwapsValidChanged();
                        }
                    };

                    vm.PropertyChanged += (_, e) =>
                    {
                        OnPropertyChanged(nameof(ModModel));
                        if (e.PropertyName is nameof(ModPaneFieldsKeySwapVm.ForwardHotkey)
                            or nameof(ModPaneFieldsKeySwapVm.BackwardHotkey)
                            or nameof(ModPaneFieldsKeySwapVm.IsValid)
                            or nameof(ModPaneFieldsKeySwapVm.SectionKey))
                        {
                            SaveModSettingsCommand.NotifyCanExecuteChanged();
                            OnPropertyChanged(nameof(CanSave));
                            NotifyAreAllKeySwapsValidChanged();
                            group.NotifyKeySwapsChanged();
                        }
                    };

                    group.KeySwaps.Add(vm);
                }

                // Snapshot initial values for change tracking
                foreach (var keySwap in group.KeySwaps)
                {
                    keySwap.UnchangedValue = keySwap.Clone();
                }

                var unchangedGroup = new IniKeySwapGroupVm
                {
                    IniFileName = group.IniFileName
                };
                foreach (var keySwap in group.KeySwaps)
                {
                    unchangedGroup.KeySwaps.Add(keySwap.Clone());
                }
                group.UnchangedValue = unchangedGroup;

                newGroups.Add(group);
            }
        }

        // Synchronize in-place to avoid a destructive blank frame
        for (int i = 0; i < newGroups.Count; i++)
        {
            if (i < IniKeySwapGroups.Count)
            {
                IniKeySwapGroups[i] = newGroups[i];
            }
            else
            {
                IniKeySwapGroups.Add(newGroups[i]);
            }
        }

        while (IniKeySwapGroups.Count > newGroups.Count)
        {
            IniKeySwapGroups.RemoveAt(IniKeySwapGroups.Count - 1);
        }

        OnPropertyChanged(nameof(HasAnyKeySwap));
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

    private void UnloadMod()
    {
        _loadedModId = null;
        _loadedMod = null;

        if (ModModel.IsLoaded)
            ModModel.PropertyChanged -= ModModel_PropertyChanged;
        IniKeySwapGroups.CollectionChanged -= (s, _) => OnPropertyChanged(nameof(HasAnyKeySwap));

        ModModel = new ModPaneFieldsVm();
        IniKeySwapGroups.Clear();
        OnPropertyChanged(nameof(ModModel));
        OnPropertyChanged(nameof(HasAnyKeySwap));
    }

    private readonly record struct LoadModMessage
    {
        public Guid? ModId { get; init; }
        public bool Force { get; init; }
        public long Sequence { get; init; }
    }

    public void Receive(ModChangedMessage message)
    {
        if (message.sender == this)
            return;

        if (_loadedModId == null || _loadedModId == message.SkinEntry.Id || message.SkinEntry.IsEnabled)
        {
            QueueLoadMod(message.SkinEntry.Id, true);
        }
    }

    public Task OnNavigatedToAsync(DispatcherQueue dispatcherQueue, CancellationToken navigationCt)
    {
        _dispatcherQueue = dispatcherQueue;
        _cancellationToken = navigationCt;
        _ = Task.Run(ModLoaderLoopAsync, CancellationToken.None);
        Messenger.RegisterAll(this);
        BusySetter.HardBusyChanged += BusySetter_HardBusyChanged;
        _localizer.LanguageChanged += OnLanguageChanged;
        IsInitialized = true;
        return Task.CompletedTask;
    }

    public void OnNavigatedFrom()
    {
        _channel.Writer.TryComplete();
        Messenger.UnregisterAll(this);
        BusySetter.HardBusyChanged -= BusySetter_HardBusyChanged;
        _localizer.LanguageChanged -= OnLanguageChanged;

        if (ModModel.IsLoaded)
        {
            ModModel.PropertyChanged -= ModModel_PropertyChanged;
        }

        _loadedModId = null;
        _loadedMod = null;
        IniKeySwapGroups.Clear();
        ModModel = new ModPaneFieldsVm();

        try
        {
            _loadModLock.Dispose();
        }
        catch (Exception e)
        {
            _logger.Warning(e, "Failed to dispose of load mod lock");
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            foreach (var group in IniKeySwapGroups)
            {
                foreach (var swap in group.KeySwaps)
                {
                    swap.RefreshLanguage();
                }
            }
        });
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

        var pathPicker = new PathPicker()
        {
            CommitButtonText = "设置图片",
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            FileTypeFilter = [.. Constants.SupportedImageExtensions]
        };

        await pathPicker.BrowseFilePathAsync(App.MainWindow);

        if (string.IsNullOrEmpty(pathPicker.Path))
        {
            return;
        }

        var file = await StorageFile.GetFileFromPathAsync(pathPicker.Path);

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

            if (_dispatcherQueue.HasThreadAccess)
            {
                ModModel.ImageUri = imagePath;
            }
            else
            {
                _dispatcherQueue.TryEnqueue(() => ModModel.ImageUri = imagePath);
            }
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

    public bool AreModifiedKeySwapsValid
    {
        get
        {
            return IniKeySwapGroups.All(g =>
                g.KeySwaps.Where(k => k.IsKeySwapChanged()).All(k =>
                    k is { IsValid: true, SectionNameEditValid: true }));
        }
    }

    private void NotifyAreAllKeySwapsValidChanged()
    {
        OnPropertyChanged(nameof(AreAllKeySwapsValid));
        OnPropertyChanged(nameof(AreModifiedKeySwapsValid));
        NotifyCanSaveModSettingsChanged();
    }

    public bool CanSaveModSettings
    {
        get
        {
            if (!DefaultCanExecute)
                return false;

            var hasModChanges = ModModel.AnyChanges;
            var hasKeyChanges = HasKeySwapChanges();

            if (!hasModChanges && !hasKeyChanges)
                return false;

            if (hasKeyChanges && !AreModifiedKeySwapsValid)
                return false;

            return true;
        }
    }

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
                    var settingsResult = await _modSettingsService.SaveSettingsAsync(_loadedModId!.Value, updateRequest, _cancellationToken);

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
                            var forwardKeys = keySwap.ForwardKeyItems
                                .Select(k => k.RawKey.Trim())
                                .Where(k => !string.IsNullOrEmpty(k))
                                .ToList();

                            var backwardKeys = keySwap.BackwardKeyItems
                                .Select(k => k.RawKey.Trim())
                                .Where(k => !string.IsNullOrEmpty(k))
                                .ToList();

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

                    var keySwapResult = await keySwapService.SaveKeySwapsAsync(_loadedModId!.Value, updatedKeySwaps);

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
            var imageUri = new Uri(image.Path);
            if (_dispatcherQueue.HasThreadAccess)
            {
                ModModel.ImageUri = imageUri;
            }
            else
            {
                _dispatcherQueue.TryEnqueue(() => ModModel.ImageUri = imageUri);
            }
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
            if (_dispatcherQueue.HasThreadAccess)
            {
                ModModel.ImageUri = filePath;
            }
            else
            {
                _dispatcherQueue.TryEnqueue(() => ModModel.ImageUri = filePath);
            }
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

    public string IsKeySwapManagementEnabled => (!IgnoreMergedIni).ToString().ToLower();

    private ModPaneFieldsVm(CharacterSkinEntry modEntry, ModSettings modSettings)
    {
        IsEnabled = modEntry.IsEnabled;
        ImageUri = modSettings.ImagePath ?? ImageHandlerService.StaticPlaceholderImageUri;
        ModDisplayName = modEntry.Mod.GetDisplayName();
        ModUrl = modSettings.ModUrl?.ToString() ?? "";
        ModIniPath = modSettings.MergedIniPath?.ToString();
        IgnoreMergedIni = modSettings.IgnoreMergedIni;

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AnyChanges))
                OnPropertyChanged(nameof(AnyChanges));
        };
    }

    public ModPaneFieldsVm()
    {
    }

    public static ModPaneFieldsVm FromModEntry(CharacterSkinEntry modEntry, ModSettings modSettings, ICollection<KeySwapSection>? keySwaps = null)
    {
        var unchangedValue = new ModPaneFieldsVm(modEntry, modSettings)
        {
            IsLoaded = true,
            UnchangedValue = null
        };

        var result = new ModPaneFieldsVm(modEntry, modSettings)
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
                   IgnoreMergedIni != UnchangedValue.IgnoreMergedIni;
        }
    }
}

// ini keyswap 分组 ViewModel
public partial class IniKeySwapGroupVm
    : ObservableObject
{
    [ObservableProperty] private string _iniFileName = string.Empty;
    [ObservableProperty] private bool _isExpanded = true;

    public ObservableCollection<ModPaneFieldsKeySwapVm> KeySwaps { get; } = [];

    // 用于变更检测的原始值
    public IniKeySwapGroupVm? UnchangedValue { get; set; }

    public string? ModPath { get; set; }

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

            return KeySwaps.Any(ks => ks.IsKeySwapChanged());
        }
    }

    public void NotifyKeySwapsChanged()
    {
        OnPropertyChanged(nameof(IsKeySwapsChanged));
    }
}

public partial class KeyItemVm : ObservableObject
{
    [ObservableProperty] private string _rawKey = string.Empty;
    [ObservableProperty] private string _friendlyText = string.Empty;
    [ObservableProperty] private bool _isUnsaved;
    [ObservableProperty] private bool _isNotLastItem;
    [ObservableProperty] private bool _canDeleteKey;
    [ObservableProperty] private bool _canAddReverseKey;
    [ObservableProperty] private bool _canAddAlternative = true;
    [ObservableProperty] private bool _isRequestingFocus;

    public ModPaneFieldsKeySwapVm? Parent { get; set; }
    public bool IsForward { get; set; } = true;

    public KeyItemVm()
    {
    }

    public KeyItemVm(string rawKey, bool isUnsaved = false)
    {
        _rawKey = rawKey;
        _friendlyText = KeyMappingHelpers.ConvertToFriendlyText(rawKey, IsCurrentLanguageChinese);
        _isUnsaved = isUnsaved;
    }

    partial void OnRawKeyChanged(string value)
    {
        FriendlyText = KeyMappingHelpers.ConvertToFriendlyText(value, IsCurrentLanguageChinese);
    }

    public void RefreshFriendlyText()
    {
        FriendlyText = KeyMappingHelpers.ConvertToFriendlyText(RawKey, IsCurrentLanguageChinese);
        OnPropertyChanged(nameof(OrSeparatorText));
    }

    public string OrSeparatorText => IsCurrentLanguageChinese ? "或" : "or";

    public static bool IsCurrentLanguageChinese =>
        (App.GetService<ILanguageLocalizer>()?.CurrentLanguage?.LanguageCode ?? System.Globalization.CultureInfo.CurrentUICulture.Name)
        .StartsWith("zh", StringComparison.OrdinalIgnoreCase);
}

public partial class ModPaneFieldsKeySwapVm : ObservableObject
{
    [ObservableProperty] private string _sectionKey = string.Empty;
    [ObservableProperty] private string? _condition;
    [ObservableProperty] private string? _forwardHotkey;
    [ObservableProperty] private string? _backwardHotkey;
    [ObservableProperty] private string? _type;
    [ObservableProperty] private string _variationsCount = "0";


    public ObservableCollection<KeyItemVm> ForwardKeyItems { get; } = [];
    public ObservableCollection<KeyItemVm> BackwardKeyItems { get; } = [];
    private bool _isInternalKeySyncing;


    public ModPaneFieldsKeySwapVm? UnchangedValue { get; set; }


    [ObservableProperty] private string? _forwardHotkeyFriendlyText;
    [ObservableProperty] private string? _backwardHotkeyFriendlyText;

    public ModPaneFieldsKeySwapVm()
    {
        ForwardKeyItems.CollectionChanged += (s, e) => NotifyForwardKeysUpdated();
        BackwardKeyItems.CollectionChanged += (s, e) => NotifyBackwardKeysUpdated();
    }

    public bool HasForwardKeys => ForwardKeyItems.Count > 0;
    public bool HasBackwardKeys => BackwardKeyItems.Count > 0;
    public bool IsChanged => IsKeySwapChanged();

    public void UpdateKeyItemStates()
    {
        var originalForward = UnchangedValue?.ForwardKeyItems.Select(k => k.RawKey).ToList() ?? [];
        for (int i = 0; i < ForwardKeyItems.Count; i++)
        {
            var item = ForwardKeyItems[i];
            item.Parent = this;
            item.IsForward = true;
            item.IsNotLastItem = i < ForwardKeyItems.Count - 1;
            item.CanDeleteKey = ForwardKeyItems.Count > 1;
            item.CanAddReverseKey = item.IsForward && IsBackKeySupported && BackwardKeyItems.Count == 0;
            item.CanAddAlternative = true;

            if (UnchangedValue != null)
            {
                item.IsUnsaved = i >= originalForward.Count ||
                    !string.Equals(item.RawKey.Trim(), originalForward[i].Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        var originalBackward = UnchangedValue?.BackwardKeyItems.Select(k => k.RawKey).ToList() ?? [];
        for (int i = 0; i < BackwardKeyItems.Count; i++)
        {
            var item = BackwardKeyItems[i];
            item.Parent = this;
            item.IsForward = false;
            item.IsNotLastItem = i < BackwardKeyItems.Count - 1;
            item.CanDeleteKey = true;
            item.CanAddReverseKey = false;
            item.CanAddAlternative = true;

            if (UnchangedValue != null)
            {
                item.IsUnsaved = i >= originalBackward.Count ||
                    !string.Equals(item.RawKey.Trim(), originalBackward[i].Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    public void NotifyForwardKeysUpdated()
    {
        if (_isInternalKeySyncing) return;
        _isInternalKeySyncing = true;
        try
        {
            ForwardHotkey = KeyMappingHelpers.FormatKeyList(ForwardKeyItems.Select(k => k.RawKey));
        }
        finally
        {
            _isInternalKeySyncing = false;
        }
        ForwardHotkeyFriendlyText = ConvertToFriendlyText(ForwardHotkey);
        UpdateKeyItemStates();
        OnPropertyChanged(nameof(ForwardHotkeyValid));
        OnPropertyChanged(nameof(HasForwardKeys));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(IsForwardKeyUnsaved));
    }

    public void NotifyBackwardKeysUpdated()
    {
        if (_isInternalKeySyncing) return;
        _isInternalKeySyncing = true;
        try
        {
            BackwardHotkey = KeyMappingHelpers.FormatKeyList(BackwardKeyItems.Select(k => k.RawKey));
        }
        finally
        {
            _isInternalKeySyncing = false;
        }
        BackwardHotkeyFriendlyText = ConvertToFriendlyText(BackwardHotkey);
        UpdateKeyItemStates();
        OnPropertyChanged(nameof(BackwardHotkeyValid));
        OnPropertyChanged(nameof(HasBackwardKeys));
        OnPropertyChanged(nameof(IsBackwardKeyVisible));
        OnPropertyChanged(nameof(IsChanged));
        OnPropertyChanged(nameof(IsBackwardKeyUnsaved));
    }

    public bool IsForwardKeyUnsaved => UnchangedValue != null && !KeyMappingHelpers.AreKeyListsEqual(
        ForwardKeyItems.Select(k => k.RawKey),
        UnchangedValue.ForwardKeyItems.Select(k => k.RawKey));

    public bool IsBackwardKeyUnsaved => UnchangedValue != null && !KeyMappingHelpers.AreKeyListsEqual(
        BackwardKeyItems.Select(k => k.RawKey),
        UnchangedValue.BackwardKeyItems.Select(k => k.RawKey));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBackwardKeyVisible))]
    private bool _isBackwardKeyExpanded;

    public bool IsBackwardKeyVisible => IsBackKeySupported && (BackwardKeyItems.Count > 0 || IsBackwardKeyExpanded);

    public void AddForwardAlternativeKey()
    {
        var newItem = new KeyItemVm(string.Empty, isUnsaved: true)
        {
            Parent = this,
            IsForward = true,
            IsRequestingFocus = true
        };
        ForwardKeyItems.Add(newItem);
        UpdateKeyItemStates();
        NotifyForwardKeysUpdated();
    }

    public void AddBackwardAlternativeKey()
    {
        var newItem = new KeyItemVm(string.Empty, isUnsaved: true)
        {
            Parent = this,
            IsForward = false,
            IsRequestingFocus = true
        };
        BackwardKeyItems.Add(newItem);
        UpdateKeyItemStates();
        NotifyBackwardKeysUpdated();
    }

    public void ExpandBackwardKeyForRecording()
    {
        IsBackwardKeyExpanded = true;
        if (BackwardKeyItems.Count == 0)
        {
            BackwardKeyItems.Add(new KeyItemVm(string.Empty, isUnsaved: true)
            {
                Parent = this,
                IsForward = false,
                IsRequestingFocus = true
            });
            UpdateKeyItemStates();
            NotifyBackwardKeysUpdated();
        }
    }

    public void CollapseBackwardKey()
    {
        IsBackwardKeyExpanded = false;
    }



    public void ApplyDirectKey(string newRawKey, bool isForward)
    {
        var targetList = isForward ? ForwardKeyItems : BackwardKeyItems;
        if (targetList.Count > 0)
        {
            ApplyKeyItemChange(targetList[0], newRawKey, isForward);
        }
        else
        {
            var newItem = new KeyItemVm(newRawKey, isUnsaved: true) { Parent = this, IsForward = isForward };
            targetList.Add(newItem);
            if (isForward)
                NotifyForwardKeysUpdated();
            else
                NotifyBackwardKeysUpdated();
        }
    }

    public void ApplyKeyItemChange(KeyItemVm item, string newRawKey, bool isForward)
    {
        item.RawKey = newRawKey;
        if (isForward)
            NotifyForwardKeysUpdated();
        else
            NotifyBackwardKeysUpdated();
    }

    public void AddAlternativeKey(KeyItemVm currentItem, bool isForward)
    {
        var targetList = isForward ? ForwardKeyItems : BackwardKeyItems;
        var index = targetList.IndexOf(currentItem);
        var newItem = new KeyItemVm(string.Empty, isUnsaved: true)
        {
            Parent = this,
            IsForward = isForward,
            IsRequestingFocus = true
        };

        if (index >= 0 && index < targetList.Count)
        {
            targetList.Insert(index + 1, newItem);
        }
        else
        {
            targetList.Add(newItem);
        }

        if (isForward)
            NotifyForwardKeysUpdated();
        else
            NotifyBackwardKeysUpdated();
    }

    public void DeleteKeyItem(KeyItemVm item, bool isForward)
    {
        var targetList = isForward ? ForwardKeyItems : BackwardKeyItems;
        if (isForward)
        {
            if (targetList.Count > 1)
            {
                targetList.Remove(item);
            }
            else
            {
                item.RawKey = string.Empty;
            }
        }
        else
        {
            targetList.Remove(item);
            if (targetList.Count == 0)
            {
                IsBackwardKeyExpanded = false;
            }
        }

        if (isForward)
            NotifyForwardKeysUpdated();
        else
            NotifyBackwardKeysUpdated();
    }

    public void LoadKeys(IEnumerable<string>? forwardKeys, IEnumerable<string>? backwardKeys)
    {
        _isInternalKeySyncing = true;
        try
        {
            ForwardKeyItems.Clear();
            var normalizedForward = KeyMappingHelpers.NormalizeKeys(forwardKeys);
            if (normalizedForward.Count == 0)
            {
                ForwardKeyItems.Add(new KeyItemVm(string.Empty) { Parent = this, IsForward = true });
            }
            else
            {
                foreach (var key in normalizedForward)
                {
                    ForwardKeyItems.Add(new KeyItemVm(key) { Parent = this, IsForward = true });
                }
            }
            ForwardHotkey = KeyMappingHelpers.FormatKeyList(ForwardKeyItems.Select(k => k.RawKey));

            BackwardKeyItems.Clear();
            var normalizedBackward = KeyMappingHelpers.NormalizeKeys(backwardKeys);
            foreach (var key in normalizedBackward)
            {
                BackwardKeyItems.Add(new KeyItemVm(key) { Parent = this, IsForward = false });
            }
            BackwardHotkey = KeyMappingHelpers.FormatKeyList(BackwardKeyItems.Select(k => k.RawKey));
        }
        finally
        {
            _isInternalKeySyncing = false;
        }

        ForwardHotkeyFriendlyText = ConvertToFriendlyText(ForwardHotkey);
        BackwardHotkeyFriendlyText = ConvertToFriendlyText(BackwardHotkey);
        UpdateKeyItemStates();
        OnPropertyChanged(nameof(ForwardHotkeyValid));
        OnPropertyChanged(nameof(BackwardHotkeyValid));
        OnPropertyChanged(nameof(HasForwardKeys));
        OnPropertyChanged(nameof(HasBackwardKeys));
        OnPropertyChanged(nameof(IsBackKeySupported));
        OnPropertyChanged(nameof(ActionLabelText));
        OnPropertyChanged(nameof(BackActionLabelText));
        OnPropertyChanged(nameof(ModeDisplayText));
        OnPropertyChanged(nameof(ModeBadgeToolTipText));
        OnPropertyChanged(nameof(SectionToolTipText));
        OnPropertyChanged(nameof(IsBackwardKeyVisible));
        OnPropertyChanged(nameof(IsForwardKeyUnsaved));
        OnPropertyChanged(nameof(IsBackwardKeyUnsaved));
        OnPropertyChanged(nameof(IsValid));
    }

    public ModPaneFieldsKeySwapVm Clone()
    {
        var clone = new ModPaneFieldsKeySwapVm
        {
            SectionKey = SectionKey,
            Type = Type,
            VariationsCount = VariationsCount
        };
        clone.LoadKeys(
            ForwardKeyItems.Select(k => k.RawKey),
            BackwardKeyItems.Select(k => k.RawKey)
        );
        return clone;
    }


    public bool SectionNameEditValid
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SectionNameForEdit?.Trim()))
                return false;

            // If the section name was not modified, treat it as valid even if it contains spaces.
            if (UnchangedValue != null && SectionKey == UnchangedValue.SectionKey)
                return true;

            return !SectionNameForEdit.Contains(' ');
        }
    }

    public bool ForwardHotkeyValid => ForwardKeyItems.Count > 0 || !string.IsNullOrWhiteSpace(ForwardHotkey?.Trim());
    public bool BackwardHotkeyValid => BackwardKeyItems.Count > 0 || !string.IsNullOrWhiteSpace(BackwardHotkey?.Trim());

    public bool IsValid => SectionNameEditValid && ForwardHotkeyValid;

    public string DisplaySectionName
    {
        get
        {
            var sectionName = SectionKey;
            var content = sectionName.TrimStart('[').TrimEnd(']');
            return RemoveKeyPrefixes(content);
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
            var currentDisplay = SectionNameForEdit;

            // Idempotent guard: avoid updating SectionKey when formatting matches current display.
            if (string.Equals(trimmedValue, currentDisplay, StringComparison.Ordinal))
                return;

            var sectionName = SectionKey.TrimStart('[').TrimEnd(']');
            var prefix = IniConfigHelpers.GetSectionPrefix(sectionName, currentDisplay);

            var newSectionKey = string.IsNullOrWhiteSpace(trimmedValue) ? "[]" : $"[{prefix}{trimmedValue}]";
            if (SectionKey != newSectionKey)
            {
                SectionKey = newSectionKey;
                OnPropertyChanged(nameof(SectionKey));
                OnPropertyChanged(nameof(DisplaySectionName));
                OnPropertyChanged(nameof(SectionToolTipText));
                OnPropertyChanged(nameof(SectionNameForEdit));
                OnPropertyChanged(nameof(SectionNameEditValid));
                OnPropertyChanged(nameof(IsValid));
                OnPropertyChanged(nameof(IsChanged));
            }
        }
    }

    public string SectionPrefix => IniConfigHelpers.GetSectionPrefix(SectionKey.TrimStart('[').TrimEnd(']'), SectionNameForEdit);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditingSectionPreview))]
    private string _editingSectionSuffix = string.Empty;

    public string EditingSectionPreview
    {
        get
        {
            var clean = EditingSectionSuffix?.Trim().Replace(" ", "") ?? string.Empty;
            return string.IsNullOrWhiteSpace(clean) ? SectionKey : $"[{SectionPrefix}{clean}]";
        }
    }

    public void BeginEditSection()
    {
        EditingSectionSuffix = SectionNameForEdit;
    }

    public void CommitEditSection()
    {
        var clean = EditingSectionSuffix?.Trim().Replace(" ", "") ?? string.Empty;
        if (!string.IsNullOrEmpty(clean))
        {
            SectionNameForEdit = clean;
        }
    }

    public void CancelEditSection()
    {
        EditingSectionSuffix = SectionNameForEdit;
    }

    public int ParsedVariationsCount => int.TryParse(VariationsCount, out var count) ? count : 0;
    public bool IsBackKeySupported => IniConfigHelpers.IsBackKeySupported(Type, ParsedVariationsCount);

    public string ActionLabelText => IniConfigHelpers.GetActionLabel(IsBackKeySupported, KeyItemVm.IsCurrentLanguageChinese);
    public string BackActionLabelText => IniConfigHelpers.GetBackActionLabel(KeyItemVm.IsCurrentLanguageChinese);

    public string ModeDisplayText => IniConfigHelpers.GetModeDisplayText(Type, ParsedVariationsCount, KeyItemVm.IsCurrentLanguageChinese);
    public string ModeBadgeToolTipText => IniConfigHelpers.GetModeToolTipText(Type, ParsedVariationsCount, KeyItemVm.IsCurrentLanguageChinese);

    public string SectionToolTipText
    {
        get
        {
            var name = DisplaySectionName;
            var hint = KeyItemVm.IsCurrentLanguageChinese ? "点击重命名" : "Click to rename";
            if (ParsedVariationsCount > 1)
            {
                return KeyItemVm.IsCurrentLanguageChinese
                    ? $"{name} ({ParsedVariationsCount}项) · {hint}"
                    : $"{name} ({ParsedVariationsCount} items) · {hint}";
            }
            return $"{name} · {hint}";
        }
    }

    public Visibility ModeBadgeVisibility =>
        !string.IsNullOrWhiteSpace(ModeDisplayText) && !string.Equals(Type?.Trim(), "cycle", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;

    public Visibility VariationsCountVisibility => ModeBadgeVisibility;

    private static string RemoveKeyPrefixes(string name) => IniConfigHelpers.RemoveKeyPrefixes(name);

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(ActionLabelText));
        OnPropertyChanged(nameof(BackActionLabelText));
        OnPropertyChanged(nameof(ModeDisplayText));
        OnPropertyChanged(nameof(ModeBadgeToolTipText));
        OnPropertyChanged(nameof(SectionToolTipText));
        ForwardHotkeyFriendlyText = ConvertToFriendlyText(ForwardHotkey);
        BackwardHotkeyFriendlyText = ConvertToFriendlyText(BackwardHotkey);
        foreach (var item in ForwardKeyItems)
        {
            item.RefreshFriendlyText();
        }
        foreach (var item in BackwardKeyItems)
        {
            item.RefreshFriendlyText();
        }
        UpdateKeyItemStates();
    }

    public bool IsKeySwapChanged()
    {
        if (UnchangedValue is null)
            return false;

        var forwardChanged = !KeyMappingHelpers.AreKeyListsEqual(
            ForwardKeyItems.Select(k => k.RawKey),
            UnchangedValue.ForwardKeyItems.Select(k => k.RawKey));

        var backwardChanged = !KeyMappingHelpers.AreKeyListsEqual(
            BackwardKeyItems.Select(k => k.RawKey),
            UnchangedValue.BackwardKeyItems.Select(k => k.RawKey));

        var sectionChanged = (SectionKey ?? "") != (UnchangedValue.SectionKey ?? "");

        return forwardChanged || backwardChanged || sectionChanged;
    }

    partial void OnForwardHotkeyChanged(string? value)
    {
        ForwardHotkeyFriendlyText = ConvertToFriendlyText(value);
        if (!_isInternalKeySyncing)
        {
            _isInternalKeySyncing = true;
            try
            {
                ForwardKeyItems.Clear();
                var tokens = KeyMappingHelpers.SplitMultiKeys(value);
                if (tokens.Count == 0)
                {
                    ForwardKeyItems.Add(new KeyItemVm(string.Empty) { Parent = this, IsForward = true });
                }
                else
                {
                    foreach (var key in tokens)
                    {
                        ForwardKeyItems.Add(new KeyItemVm(key) { Parent = this, IsForward = true });
                    }
                }
            }
            finally
            {
                _isInternalKeySyncing = false;
            }
            UpdateKeyItemStates();
        }
        OnPropertyChanged(nameof(ForwardHotkeyValid));
        OnPropertyChanged(nameof(IsValid));
    }

    partial void OnBackwardHotkeyChanged(string? value)
    {
        BackwardHotkeyFriendlyText = ConvertToFriendlyText(value);
        if (!_isInternalKeySyncing)
        {
            _isInternalKeySyncing = true;
            try
            {
                BackwardKeyItems.Clear();
                foreach (var key in KeyMappingHelpers.SplitMultiKeys(value))
                {
                    BackwardKeyItems.Add(new KeyItemVm(key) { Parent = this, IsForward = false });
                }
            }
            finally
            {
                _isInternalKeySyncing = false;
            }
            UpdateKeyItemStates();
        }
        OnPropertyChanged(nameof(BackwardHotkeyValid));
    }

    private static string? ConvertToFriendlyText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : KeyMappingHelpers.ConvertToFriendlyText(value, KeyItemVm.IsCurrentLanguageChinese);
    }
}