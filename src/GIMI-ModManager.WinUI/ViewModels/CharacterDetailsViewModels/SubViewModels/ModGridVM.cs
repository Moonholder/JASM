using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkitWrapper;
using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.Core.Services.ModPresetService;
using GIMI_ModManager.Core.Services.ModPresetService.Models;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Helpers;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.Views;
using Serilog;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Channels;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;

public partial class ModGridVM(
    ISkinManagerService skinManagerService,
    CharacterSkinService characterSkinService,
    NotificationManager notificationService,
    ModPresetService presetService,
    ILocalSettingsService localSettingsService,
    ModNotificationManager modNotificationManager,
    ModSettingsService modSettingsService,
    ElevatorService elevatorService,
    UserPreferencesService userPreferencesService,
    IWindowManagerService windowManagerService,
    ILanguageLocalizer localizer,
    ModVariantHandlerService modVariantHandlerService)
    : ObservableRecipient, IRecipient<ModChangedMessage>
{
    private readonly ISkinManagerService _skinManagerService = skinManagerService;
    private readonly CharacterSkinService _characterSkinService = characterSkinService;
    private readonly NotificationManager _notificationService = notificationService;
    private readonly ModPresetService _presetService = presetService;
    private readonly ILocalSettingsService _localSettingsService = localSettingsService;
    private readonly ModNotificationManager _modNotificationManager = modNotificationManager;
    private readonly ModSettingsService _modSettingsService = modSettingsService;
    private readonly ElevatorService _elevatorService = elevatorService;
    private readonly UserPreferencesService _userPreferencesService = userPreferencesService;
    private readonly IWindowManagerService _windowManagerService = windowManagerService;
    private readonly ModVariantHandlerService _modVariantHandlerService = modVariantHandlerService;
    private readonly ILogger _logger = Log.ForContext<ModGridVM>();
    private readonly ILanguageLocalizer _localizer = localizer;

    private DispatcherQueue? _dispatcherQueue; // 设为可空
    private CancellationToken _navigationCt = default;
    private ICharacterModList? _modList; // 设为可空
    private ModDetailsPageContext _context = null!;
    private readonly AsyncLock _modRefreshLock = new();
    public BusySetter BusySetter { get; set; } = null!; // 假设会被父 VM 注入
    public bool IsDescendingSort { get; private set; } = true;
    public ModGridSortingMethod CurrentSortingMethod { get; private set; } = new(ModRowSorter.IsEnabledSorter);

    // 防止重复 Dispose
    private bool _isDisposed = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy = true;

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty] private DataGridSelectionMode _gridSelectionMode = DataGridSelectionMode.Extended;
    private bool SingleSelect => GridSelectionMode == DataGridSelectionMode.Single;

    [ObservableProperty] private bool _isModFolderNameColumnVisible;

    [ObservableProperty] private bool _showMultipleModsActiveWarning;

    [ObservableProperty] private bool _autoSync3DMigotoConfig;

    public bool IsInitialized { get; private set; }

    private List<CharacterSkinEntry> _modsBackend = [];
    public List<CharacterSkinEntry> GetModsBackend() => [.. _modsBackend];
    private Dictionary<Guid, ModPreset[]> _modToPresetMapping = [];
    private readonly List<ModRowVM> _gridModsBackend = [];
    public List<ModRowVM> GetGridModsBackend() => [.. _gridModsBackend];
    public ObservableCollection<ModRowVM> GridMods { get; } = [];
    public ObservableCollection<ModRowVM> SelectedMods { get; } = [];

    public bool IsSingleModSelected => SelectedMods.Count == 1;
    public ModRowVM? SelectedMod => SelectedMods.Count == 1 ? SelectedMods[0] : null;

    public bool ModdableObjectHasAnyMods => _modsBackend.Count != 0;

    public int TrackedMods => _modsBackend.Count;

    private readonly Channel<QueueRefresh> _modRefreshChannel = Channel.CreateBounded<QueueRefresh>(new BoundedChannelOptions(1)
    {
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    public readonly struct QueueRefresh(TimeSpan? minWaitTime = null)
    {
        public TimeSpan? MinWaitTime { get; } = minWaitTime;
    };

    public string GetLocalized(string key, string defaultValue)
    {
        return _localizer.GetLocalizedStringOrDefault(key, defaultValue);
    }
    private async Task ModRefreshLoopAsync()
    {
        try
        {
            // Runs on the UI thread
            await foreach (var loadModMessage in _modRefreshChannel.Reader.ReadAllAsync(CancellationToken.None))
            {
                if (_navigationCt.IsCancellationRequested || _isDisposed)
                    break;
                try
                {
                    await ReloadAllModsAsync(loadModMessage.MinWaitTime);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    _notificationService?.ShowNotification(
                        _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/RefreshModsErrorTitle", "Error reloading mods"),
                        e.Message,
                        null);
                }
            }
        }
        catch (ChannelClosedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in ModRefreshLoopAsync");
        }
    }

    public async Task InitializeAsync(ModDetailsPageContext context, CancellationToken navigationCt = default)
    {
        _isDisposed = false;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _navigationCt = navigationCt;
        _context = context;

        _modList = _skinManagerService.GetCharacterModList(_context.ShownModObject);

        _modList.ModsChanged -= ModListOnModsChanged;
        _modList.ModsChanged += ModListOnModsChanged;

        _modNotificationManager.OnModNotification -= _modNotificationManager_OnModNotification;
        _modNotificationManager.OnModNotification += _modNotificationManager_OnModNotification;

        Messenger.UnregisterAll(this);
        Messenger.RegisterAll(this);

        var settings = await _localSettingsService.ReadCharacterDetailsSettingsAsync();
        GridSelectionMode = settings.SingleSelect ? DataGridSelectionMode.Single : DataGridSelectionMode.Extended;
        IsModFolderNameColumnVisible = settings.ModFolderNameColumnVisible;

        await InitModsAsync();

        if (_dispatcherQueue != null)
        {
            _ = _dispatcherQueue.EnqueueAsync(ModRefreshLoopAsync);
        }

        IsBusy = false;
        IsInitialized = true;
        OnInitialized?.Invoke(this, EventArgs.Empty);
    }

    private void _modNotificationManager_OnModNotification(object? sender, ModNotificationManager.ModNotificationEvent e)
    {
        if (_isDisposed) return;
        QueueModRefresh();
    }

    public void OnNavigateFrom()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _modRefreshChannel.Writer.TryComplete();

        Messenger.UnregisterAll(this);

        if (_modList != null)
        {
            _modList.ModsChanged -= ModListOnModsChanged;
        }
        _modNotificationManager.OnModNotification -= _modNotificationManager_OnModNotification;

        foreach (var mod in GridMods)
        {
            mod.Cleanup();
        }
        GridMods.Clear();
        SelectedMods.Clear();
        _gridModsBackend.Clear();
        _modsBackend.Clear();
        _modToPresetMapping.Clear();

        try
        {
            _modRefreshLock.Dispose();
            ToggleLock.Dispose();
        }
        catch (Exception e)
        {
            _logger.Warning(e, "Failed to dispose locks");
        }

        _modList = null;
        IsInitialized = false;
    }

    private async Task InitModsAsync()
    {
        if (_isDisposed) return;

        GridMods.Clear();
        _gridModsBackend.Clear();
        SelectedMods.Clear();

        try
        {
            await Task.Run(async () =>
            {
                if (_navigationCt.IsCancellationRequested) return;

                var refreshResult = await _skinManagerService
                    .RefreshModsAsync(_context.ShownModObject.InternalName, ct: _navigationCt)
                    .ConfigureAwait(false);

                await LoadModsAsync().ConfigureAwait(false);

                if (refreshResult.ModsDuplicate.Any() && !_isDisposed)
                {
                    var title = _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DuplicateModsDetectedTitle", "Duplicate Mods Detected");
                    var formatMessage = _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DuplicateModsDetectedMessage", "Detected duplicate mods in '{0}''s mod folder.\n");
                    var message = string.Format(formatMessage, _context.ModObjectDisplayName);

                    var formatItem = _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DuplicateModRenamedMessage", "Mod: '{0}' was renamed to '{1}' to avoid conflicts.\n");
                    message = refreshResult.ModsDuplicate.Aggregate(message,
                        (current, duplicateMod) =>
                            current +
                            string.Format(formatItem, duplicateMod.ExistingFolderName, duplicateMod.RenamedFolderName));

                    _notificationService?.ShowNotification(title,
                        message,
                        TimeSpan.FromSeconds(10));
                }
            }, _navigationCt);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_isDisposed) return;

        GridMods.AddRange(_gridModsBackend);
        SetModSorting(CurrentSortingMethod.SortingMethodType, IsDescendingSort);
    }

    public bool QueueModRefresh(TimeSpan? minWaitTime = null) => _modRefreshChannel.Writer.TryWrite(new QueueRefresh(minWaitTime));

    public async Task ReloadAllModsAsync(TimeSpan? minimumWaitTime = null)
    {
        using var __ = await ModRefreshLockAsync();
        using var _ = BusySetter.StartHardBusy();
        var waitTime = minimumWaitTime is not null ? Task.Delay(minimumWaitTime.Value, _navigationCt) : Task.CompletedTask;

        Guid? selectedModId = SelectedMods.Count == 1 ? SelectedMods.First().Id : null;

        // For now just reuse the grid init
        await InitModsAsync();

        // Make it take at least the minimum time to show the user that something is happening
        await waitTime;

        if (selectedModId.HasValue && GridMods.Any(m => m.Id == selectedModId))
        {
            SetSelectedMod(selectedModId.Value);
        }
    }

    public async Task OnChangeSkinAsync(ModDetailsPageContext context)
    {
        using var __ = await ModRefreshLockAsync();
        _context = context;
        _gridModsBackend.Clear();
        GridMods.Clear();
        SelectedMods.Clear();

        await Task.Run(LoadModsAsync, _navigationCt);

        GridMods.AddRange(_gridModsBackend);
        SetModSorting(CurrentSortingMethod.SortingMethodType, IsDescendingSort);
    }

    private bool RefreshMultipleModsActiveWarning() =>
        ShowMultipleModsActiveWarning = !_context.ShownModObject.IsMultiMod && _modsBackend.Count(m => m.IsEnabled) > 1;

    private async Task LoadModsAsync()
    {
        if (_isDisposed) return;

        if (_context.IsCharacter && _context.Skins.Count > 1)
        {
            _modsBackend = await _characterSkinService
                .GetCharacterSkinEntriesForSkinAsync(_context.SelectedSkin, useSettingsCache: true,
                    cancellationToken: _navigationCt)
                .ToListAsync().ConfigureAwait(false);
        }
        else if (_modList != null)
        {
            _modsBackend = _modList.Mods.ToList();
        }

        if (_isDisposed) return;

        _modToPresetMapping = await _presetService
            .FindPresetsForModsAsync(_modsBackend.Select(m => m.Id))
            .ConfigureAwait(false);

        foreach (var x in _modsBackend)
        {
            if (_navigationCt.IsCancellationRequested || _isDisposed) break;

            var modVm = await CreateModRowVM(x, _navigationCt).ConfigureAwait(false);
            _gridModsBackend.Add(modVm);
        }

        _dispatcherQueue?.TryEnqueue(() =>
        {
            if (_isDisposed) return;
            RefreshMultipleModsActiveWarning();
            OnModsReloaded?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task<ModRowVM> CreateModRowVM(CharacterSkinEntry characterSkinEntry,
        CancellationToken cancellationToken = default)
    {
        var skinModSettings = await characterSkinEntry.Mod.Settings.TryReadSettingsAsync(true, cancellationToken)
            .ConfigureAwait(false);

        var presetNames = _modToPresetMapping.TryGetValue(characterSkinEntry.Id, out var presets)
            ? presets.Select(p => p.Name)
            : [];

        var modNotifications = await _modNotificationManager.GetNotificationsForModAsync(characterSkinEntry.Id);

        var variants = await _modVariantHandlerService.GetVariantsAsync(characterSkinEntry.Id).ConfigureAwait(false);
        var variantNames = variants.Select(v => v.Name);
        var activeVariant = await _modVariantHandlerService.GetActiveVariantNameAsync(characterSkinEntry.Id).ConfigureAwait(false) ?? string.Empty;

        return new ModRowVM(characterSkinEntry, skinModSettings, presetNames, modNotifications, variantNames, activeVariant)
        {
            ToggleEnabledCommand = new AsyncRelayCommand<ModRowVM>(ToggleModAsync),
            UpdateModSettingsCommand = new AsyncRelayCommand<UpdateModSettingsArgument>(UpdateModSettingsAsync)
        };
    }

    private async Task UpdateModVmAsync(CharacterSkinEntry characterSkinEntry, bool useSettingsCache = true,
        CancellationToken cancellationToken = default)
    {
        var existingMod = _gridModsBackend.FirstOrDefault(mod => mod.Id == characterSkinEntry.Id);
        if (existingMod is null)
            throw new InvalidOperationException("Mod not found in grid mods");

        var skinModSettings = await characterSkinEntry.Mod.Settings.TryReadSettingsAsync(useSettingsCache, cancellationToken);

        var presetNames = _modToPresetMapping.TryGetValue(characterSkinEntry.Id, out var presets)
            ? presets.Select(p => p.Name)
            : [];

        var modNotifications = await _modNotificationManager.GetNotificationsForModAsync(characterSkinEntry.Id);

        var variants = await _modVariantHandlerService.GetVariantsAsync(characterSkinEntry.Id);
        var variantNames = variants.Select(v => v.Name);
        var activeVariant = await _modVariantHandlerService.GetActiveVariantNameAsync(characterSkinEntry.Id) ?? string.Empty;

        void UpdateUi()
        {
            existingMod.UpdateModel(characterSkinEntry, skinModSettings, presetNames, modNotifications, variantNames, activeVariant);
            existingMod.TriggerPropertyChanged(nameof(existingMod.IsEnabled));
        }

        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(UpdateUi);
        }
        else
        {
            UpdateUi();
        }
    }

    public ModRowVM[] SearchFilterMods(string searchText)
    {
        var modsToShow = _gridModsBackend
            .Where(x => x.SearchableText.Contains(searchText, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();

        var modsToHide = _gridModsBackend.Except(modsToShow).ToArray();

        foreach (var mod in modsToShow)
        {
            if (GridMods.Contains(mod))
                continue;

            GridMods.Add(mod);
        }

        foreach (var modToHide in modsToHide)
            GridMods.Remove(modToHide);

        return GridMods.ToArray();
    }

    public void ResetModView()
    {
        var selectedMod = SelectedMods.FirstOrDefault();
        GridMods.Clear();
        GridMods.AddRange(_gridModsBackend);

        if (selectedMod is not null)
            SetSelectedMod(selectedMod.Id);
    }

    public void Receive(ModChangedMessage message)
    {
        if (message.sender == this)
            return;

        var existingMod = _gridModsBackend.FirstOrDefault(mod => mod.Id == message.SkinEntry.Id);
        if (existingMod is null)
            return;
        _ = UpdateModVmAsync(message.SkinEntry, cancellationToken: CancellationToken.None);
    }

    public void SetSelectedMod(Guid modId, bool ignoreFilters = false)
    {
        if (ignoreFilters)
            throw new NotImplementedException();

        var mod = GridMods.FirstOrDefault(x => x.Id == modId);
        if (mod is null)
            return;

        var index = GridMods.IndexOf(mod);
        SelectModEvent?.Invoke(this, new SelectModRowEventArgs(index));
    }


    public readonly AsyncLock ToggleLock = new();

    private async Task ToggleModAsync(ModRowVM? modVmToToggle)
    {
        if (modVmToToggle is null || _isDisposed) return;

        using var _ = BusySetter.StartSoftBusy();

        var modEntryToToggle = _modsBackend.FirstOrDefault(x => x.Id == modVmToToggle.Id);
        if (modEntryToToggle is null) return;
        if (_modList is null) return;

        var modsToDisable = SingleSelect
        ? [.. _modsBackend.Where(mod => modVmToToggle.Id != mod.Id && mod.IsEnabled)]
        : new List<CharacterSkinEntry>();

        List<CharacterSkinEntry> modsToSync = [];

        if (AutoSync3DMigotoConfig)
        {
            modsToSync.AddRange(modsToDisable);
            if (modEntryToToggle.IsEnabled)
            {
                modsToSync.Add(modEntryToToggle);
            }
        }

        try
        {
            using (await ToggleLock.LockAsync(cancellationToken: _navigationCt))
            {
                await Task.Run(() =>
                {
                    foreach (var skinEntry in modsToDisable)
                    {
                        if (skinEntry.IsEnabled)
                            _modList.DisableMod(skinEntry.Id);
                    }

                    _modList.ToggleMod(modEntryToToggle.Id);

                }, CancellationToken.None);

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
                                try
                                {
                                    await _userPreferencesService.SyncPreferencesToModLocalFilesAsync(mod.Id);
                                }
                                catch (Exception ex)
                                {
                                    _logger.Error(ex, "Failed to sync preferences for mod {ModName}", mod.Mod.Name);
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error during AutoSync sequence");
                    }
                }

                await UpdateModVmAsync(modEntryToToggle, false, CancellationToken.None);
                Messenger.Send(new ModChangedMessage(this, modEntryToToggle, null));

                foreach (var otherModEntry in modsToDisable)
                {
                    await UpdateModVmAsync(otherModEntry, false, CancellationToken.None);
                    Messenger.Send(new ModChangedMessage(this, otherModEntry, null));
                }

                RefreshMultipleModsActiveWarning();
                Messenger.Send(new ModChangedMessage(this, modEntryToToggle, null));
            }
        }
        catch (Exception e)
        {
            var message = e.Message.Contains("Access to the path") || e.Message.Contains("UnauthorizedAccess_IODenied_Path") ?
                _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/ToggleModFailed", "Cannot toggle mod state, target file is being used by another program.\n\nPlease:\n1. Close the mod folder opened in Resource Manager\n2. Close any program that may be editing the mod files\n3. Try again later") : e.Message;
            _notificationService.ShowNotification(_localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/OperationFailed", "Operation Failed"), message, TimeSpan.FromSeconds(8));

            await UpdateModVmAsync(modEntryToToggle, false, CancellationToken.None);
            Messenger.Send(new ModChangedMessage(this, modEntryToToggle, null));

            foreach (var otherModEntry in modsToDisable)
            {
                await UpdateModVmAsync(otherModEntry, false, CancellationToken.None);
                Messenger.Send(new ModChangedMessage(this, otherModEntry, null));
            }
        }
    }

    private async Task UpdateModSettingsAsync(UpdateModSettingsArgument? arg)
    {
        if (arg is null)
            return;

        var (modVm, updateSettingsRequest, messageBody) = arg;

        using var _ = BusySetter.StartSoftBusy();

        var mod = _modsBackend.FirstOrDefault(x => x.Id == modVm.Id);
        if (mod is null)
            return;


        var result = await Task.Run(
            async () => await _modSettingsService.SaveSettingsAsync(mod.Id, updateSettingsRequest, CancellationToken.None).ConfigureAwait(false),
            CancellationToken.None);

        if (result.IsError)
        {
            if (result.HasNotification)
                _notificationService.ShowNotification(result.Notification);
            else
                _notificationService.ShowNotification(
                    _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/SaveSettingsFailed", "Failed to save mod settings"),
                    result.Exception?.ToString() ?? result.ErrorMessage ?? "",
                    TimeSpan.FromSeconds(6));
        }

        if (result.IsSuccess)
        {
            _notificationService.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/SettingsSaved", "Mod settings saved"),
                messageBody,
                TimeSpan.FromSeconds(3));
        }

        await UpdateModVmAsync(mod, true, CancellationToken.None);
        Messenger.Send(new ModChangedMessage(this, mod, null));
    }

    public record UpdateModSettingsArgument(ModRowVM ModVm, UpdateSettingsRequest UpdateSettingsRequest, string MessageBody = "");

    public void ClearSelection() => SelectModEvent?.Invoke(this, new SelectModRowEventArgs(-1));

    [RelayCommand]
    private async Task OpenNewModsWindowAsync(object? modNotification)
    {
        if (modNotification is not ModRowVM_ModNotificationVM notification)
        {
            _logger.Warning("OpenNewModsWindowAsync called with null modModel.");
            return;
        }

        if (_modList is null)
        {
            return;
        }

        var skinEntry = _modList.Mods.FirstOrDefault(mod => mod.Id == notification.ModId);

        if (skinEntry is null)
        {
            return;
        }

        var existingWindow = _windowManagerService.GetWindow(notification.Id);
        if (existingWindow is not null)
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(async () =>
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            {
                await Task.Delay(100);
                existingWindow.BringToFront();
            });
            return;
        }


        var formatTitle = _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/NewModFilesAvailable", "New mod files available: {0}");
        var modWindow = new ModUpdateAvailableWindow(notification.Id)
        {
            Title = string.Format(formatTitle, ModFolderHelpers.GetFolderNameWithoutDisabledPrefix(skinEntry.Mod.Name))
        };
        _windowManagerService.CreateWindow(modWindow, identifier: notification.Id);
        await Task.Delay(100);
        modWindow.BringToFront();
    }

    private void ModListOnModsChanged(object? sender, ModFolderChangedArgs e)
    {
        if (!IsInitialized || _isDisposed)
            return;

        _dispatcherQueue?.TryEnqueue(() =>
        {
            var formatTitle = _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/FolderActivityDetected", "Detected folder activity in '{0}''s mod folder");
            var title = string.Format(formatTitle, _context.ShownModObject.DisplayName);
            var message = _localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/FolderActivityDetectedMessage", "Files/folders in the character's mod folder have been modified; mods have been refreshed.");
            _notificationService?.ShowNotification(title, message, TimeSpan.FromSeconds(5));
        });

        QueueModRefresh();
    }


    // Only to be used by code behind
    // Contrary to my attempt at MVVM, this is the only way to get the selected mods from the view
    // DataGrid is quite limited in this regard, so it is in charge of the selection and the view model just listens
    public void SelectionChanged_EventHandler(ICollection<ModRowVM> selectedMods, ICollection<ModRowVM> removedMods)
    {
        var anyChanges = false;
        if (selectedMods.Any())
        {
            foreach (var mod in selectedMods)
            {
                if (!SelectedMods.Contains(mod))
                {
                    SelectedMods.Add(mod);
                    anyChanges = true;
                }
            }
        }

        if (removedMods.Any())
        {
            foreach (var mod in removedMods)
            {
                if (SelectedMods.Contains(mod))
                {
                    SelectedMods.Remove(mod);
                    anyChanges = true;
                }
            }
        }

        if (anyChanges)
        {
            OnPropertyChanged(nameof(IsSingleModSelected));
            OnPropertyChanged(nameof(SelectedMod));
            OnModsSelected?.Invoke(this, new ModRowSelectedEventArgs(SelectedMods));
        }
    }

    // Only to be used by code behind
    public async Task OnKeyDown_EventHandlerAsync(VirtualKey key)
    {
        if (BusySetter.IsHardBusy)
            return;

        var selectedMods = SelectedMods.ToArray();
        if (selectedMods.Length == 0)
            return;

        if (SingleSelect && selectedMods.Length > 1)
        {
            selectedMods = selectedMods.Take(1).ToArray();
            Debugger.Break(); // TODO Handle
        }

        if (key == VirtualKey.Delete)
        {
            DeleteModKeyTriggered?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (key == VirtualKey.Space)
        {
            using var _ = BusySetter.StartSoftBusy();
            if (selectedMods.Any(m => !m.ToggleEnabledCommand.CanExecute(null)))
                return;

            foreach (var mod in selectedMods)
            {
                await mod.ToggleEnabledCommand.ExecuteAsync(mod);
            }
        }
    }

    #region Events

    public event EventHandler<ModRowSelectedEventArgs>? OnModsSelected;

    // Set single selected from code
    public event EventHandler<SelectModRowEventArgs>? SelectModEvent;
    public event EventHandler? DeleteModKeyTriggered;

    public event EventHandler<SortEvent>? SortEvent;

    public event EventHandler? OnModsReloaded;

    public event EventHandler? OnInitialized;

    public class ModRowSelectedEventArgs(IEnumerable<ModRowVM> mods) : EventArgs
    {
        public ICollection<ModRowVM> Mods { get; } = mods.ToArray();
    }

    public class SelectModRowEventArgs(int index) : EventArgs
    {
        public int Index { get; } = index;
    }

    #endregion

    // Used by parent view model

    public void SetModSorting(string sortColumn, bool isDescending, bool isUiTriggered = false)
    {
        if (sortColumn.IsNullOrEmpty())
            return;

        var sorter = ModGridSortingMethod.AllSorters.FirstOrDefault(x => x.SortingMethodType == sortColumn);

        if (sorter is null)
            return;

        CurrentSortingMethod = new ModGridSortingMethod(sorter);
        IsDescendingSort = isDescending;
        SortMods();

        if (isUiTriggered == false)
            SortEvent?.Invoke(this, new SortEvent(sortColumn, isDescending));
    }

    private void SortMods()
    {
        var sortedBackendMods = CurrentSortingMethod.Sort(_gridModsBackend, IsDescendingSort).ToArray();
        _gridModsBackend.Clear();
        _gridModsBackend.AddRange(sortedBackendMods);

        var sortedVisibleMods = CurrentSortingMethod.Sort(GridMods, IsDescendingSort).ToArray();
        for (var newPosition = 0; newPosition < sortedVisibleMods.Length; newPosition++)
        {
            var mod = sortedVisibleMods[newPosition];
            var oldPosition = GridMods.IndexOf(mod);

            if (oldPosition == newPosition)
                continue;

            GridMods.Move(oldPosition, newPosition);
        }
    }

    [RelayCommand]
    private async Task SaveVariantAsync(ModRowVM modRowVm)
    {
        if (modRowVm == null) return;

        var textBox = new TextBox
        {
            PlaceholderText = _localizer.GetLocalizedStringOrDefault("/ModVariant/InputNamePlaceholder", "Enter variant name"),
            AcceptsReturn = false
        };

        var dialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("/ModVariant/InputNameTitle", "Save as Variant"),
            Content = textBox,
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common_Save", "Save"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var result = await _windowManagerService.ShowDialogAsync(dialog);
        if (result != ContentDialogResult.Primary) return;

        var name = textBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            _notificationService.ShowNotification(
                _localizer.GetLocalizedStringOrDefault("/ModVariant/InvalidNameTitle", "Invalid Name"),
                _localizer.GetLocalizedStringOrDefault("/ModVariant/InvalidNameMessage", "Variant name cannot be empty"),
                TimeSpan.FromSeconds(5)
            );
            return;
        }

        var existingVariants = await _modVariantHandlerService.GetVariantsAsync(modRowVm.Id).ConfigureAwait(true);
        if (existingVariants.Any(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            var overwriteDialog = new ContentDialog
            {
                Title = _localizer.GetLocalizedStringOrDefault("/ModVariant/OverwriteTitle", "Overwrite Variant"),
                Content = string.Format(_localizer.GetLocalizedStringOrDefault("/ModVariant/OverwriteMessage", "Variant '{0}' already exists. Do you want to overwrite it?"), name),
                PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common_Yes", "Yes"),
                CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common_Cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = App.MainWindow.Content.XamlRoot
            };

            var overwriteResult = await _windowManagerService.ShowDialogAsync(overwriteDialog);
            if (overwriteResult != ContentDialogResult.Primary) return;
        }

        var handlerResult = await _modVariantHandlerService.SaveCurrentAsVariantAsync(modRowVm.Id, name, CancellationToken.None).ConfigureAwait(true);
        if (handlerResult.IsSuccess)
        {
            if (handlerResult.HasNotification)
            {
                _notificationService.ShowNotification(handlerResult.Notification);
            }
            var skinEntry = _modList?.Mods?.FirstOrDefault(m => m.Id == modRowVm.Id);
            if (skinEntry != null)
            {
                await UpdateModVmAsync(skinEntry, useSettingsCache: false, CancellationToken.None).ConfigureAwait(true);
            }
        }
        else
        {
            if (handlerResult.HasNotification)
            {
                _notificationService.ShowNotification(handlerResult.Notification);
            }
        }
    }

    [RelayCommand]
    private async Task UpdateActiveVariantAsync()
    {
        var modRowVm = SelectedMod;
        if (modRowVm == null) return;
        var variantName = modRowVm.ActiveVariant;
        if (string.IsNullOrEmpty(variantName)) return;

        var confirmDialog = new ContentDialog
        {
            Title = _localizer.GetLocalizedStringOrDefault("/ModVariant/UpdateConfirmTitle", "Update Variant"),
            Content = string.Format(_localizer.GetLocalizedStringOrDefault("/ModVariant/UpdateConfirmMessage", "Are you sure you want to update the variant preset '{0}' with current settings?"), variantName),
            PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("Common_Yes", "Yes"),
            CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common_Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var confirmResult = await _windowManagerService.ShowDialogAsync(confirmDialog);
        if (confirmResult != ContentDialogResult.Primary) return;

        var handlerResult = await _modVariantHandlerService.SaveCurrentAsVariantAsync(modRowVm.Id, variantName, CancellationToken.None).ConfigureAwait(true);
        if (handlerResult.IsSuccess)
        {
            if (handlerResult.HasNotification)
            {
                _notificationService.ShowNotification(handlerResult.Notification);
            }
            var skinEntry = _modList?.Mods?.FirstOrDefault(m => m.Id == modRowVm.Id);
            if (skinEntry != null)
            {
                await UpdateModVmAsync(skinEntry, useSettingsCache: false, CancellationToken.None).ConfigureAwait(true);
            }
        }
        else
        {
            if (handlerResult.HasNotification)
            {
                _notificationService.ShowNotification(handlerResult.Notification);
            }
        }
    }

    [RelayCommand]
    private async Task ApplyVariantAsync(string variantName)
    {
        if (string.IsNullOrEmpty(variantName)) return;
        var modRowVm = SelectedMod;
        if (modRowVm == null) return;

        var handlerResult = await _modVariantHandlerService.ApplyVariantAsync(modRowVm.Id, variantName, CancellationToken.None).ConfigureAwait(true);
        if (handlerResult.IsSuccess)
        {
            var skinEntry = _modList?.Mods?.FirstOrDefault(m => m.Id == modRowVm.Id);
            if (skinEntry != null)
            {
                await UpdateModVmAsync(skinEntry, useSettingsCache: false, CancellationToken.None).ConfigureAwait(true);
                Messenger.Send(new ModChangedMessage(this, skinEntry, null));
            }
        }
        else
        {
            if (handlerResult.HasNotification)
            {
                _notificationService.ShowNotification(handlerResult.Notification);
            }
        }
    }

    [RelayCommand]
    private async Task RenameVariantAsync(string newName)
    {
        if (string.IsNullOrEmpty(newName)) return;
        var modRowVm = SelectedMod;
        if (modRowVm == null) return;
        var oldName = modRowVm.ActiveVariant;
        if (string.IsNullOrEmpty(oldName) || oldName == newName) return;

        var handlerResult = await _modVariantHandlerService.RenameVariantAsync(modRowVm.Id, oldName, newName, CancellationToken.None).ConfigureAwait(true);
        if (handlerResult.IsSuccess)
        {
            if (handlerResult.HasNotification)
            {
                _notificationService.ShowNotification(handlerResult.Notification);
            }
            var skinEntry = _modList?.Mods?.FirstOrDefault(m => m.Id == modRowVm.Id);
            if (skinEntry != null)
            {
                await UpdateModVmAsync(skinEntry, useSettingsCache: false, CancellationToken.None).ConfigureAwait(true);
            }
        }
        else
        {
            if (handlerResult.HasNotification)
            {
                _notificationService.ShowNotification(handlerResult.Notification);
            }
        }
    }

    [RelayCommand]
    private async Task DeleteVariantAsync()
    {
        var modRowVm = SelectedMod;
        if (modRowVm == null) return;
        var variantName = modRowVm.ActiveVariant;
        if (string.IsNullOrEmpty(variantName)) return;

        var handlerResult = await _modVariantHandlerService.DeleteVariantAsync(modRowVm.Id, variantName, CancellationToken.None).ConfigureAwait(true);
        if (handlerResult.IsSuccess)
        {
            if (handlerResult.HasNotification)
            {
                _notificationService.ShowNotification(handlerResult.Notification);
            }
            var skinEntry = _modList?.Mods?.FirstOrDefault(m => m.Id == modRowVm.Id);
            if (skinEntry != null)
            {
                await UpdateModVmAsync(skinEntry, useSettingsCache: false, CancellationToken.None).ConfigureAwait(true);
            }
        }
        else
        {
            if (handlerResult.HasNotification)
            {
                _notificationService.ShowNotification(handlerResult.Notification);
            }
        }
    }

    private Task<LockReleaser> ModRefreshLockAsync() => _modRefreshLock.LockAsync(null, _navigationCt);
}

public class SortEvent(string sortColumn, bool isDescending) : EventArgs
{
    public string SortColumn { get; } = sortColumn;
    public bool IsDescending { get; } = isDescending;
}