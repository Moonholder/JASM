using System.Collections.Concurrent;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.GamesService.Models;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services.ModPresetService;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Contracts.ViewModels;
using GIMI_ModManager.WinUI.Models;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Services;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;
using GIMI_ModManager.WinUI.ViewModels.CharacterGalleryViewModels;
using Microsoft.UI.Dispatching;
using Serilog;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels;

public partial class CharacterDetailsViewModel : ObservableObject, INavigationAware
{
    private readonly INavigationService _navigationService = App.GetService<INavigationService>();
    private readonly IGameService _gameService = App.GetService<IGameService>();
    private readonly ILogger _logger = App.GetService<ILogger>().ForContext<CharacterDetailsViewModel>();
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly NotificationManager _notificationService = App.GetService<NotificationManager>();
    private readonly ILocalSettingsService _localSettingsService = App.GetService<ILocalSettingsService>();
    private readonly ModNotificationManager _modNotificationManager = App.GetService<ModNotificationManager>();
    private readonly ModDragAndDropService _modDragAndDropService = App.GetService<ModDragAndDropService>();
    private readonly IWindowManagerService _windowManagerService = App.GetService<IWindowManagerService>();
    private readonly ModPresetService _presetService = App.GetService<ModPresetService>();
    private readonly ElevatorService _elevatorService = App.GetService<ElevatorService>();

    public Func<Task>? GridLoadedAwaiter { get; set; }

    private CancellationTokenSource? _navigationCancellationTokenSource;
    public CancellationToken CancellationToken;

    private bool IsReturning => (_navigationCancellationTokenSource?.IsCancellationRequested ?? true) || _isErrorNavigateBack;
    private bool _isErrorNavigateBack;
    private ICharacterModList _modList = null!;
    [ObservableProperty] private bool _isNavigationFinished;

    private readonly BusySetter _busySetter;

    [ObservableProperty] private string _loadingItemText = "Character";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSoftBusy), nameof(IsWorking))]
    private bool _isSoftBusy; // App is doing something, but the user can still do other things


    private bool _isHardBusy; // App is doing something, and the user can't do anything on the page

    public bool IsHardBusy
    {
        get => _isHardBusy;
        set
        {
            if (value == _isHardBusy) return;
            _isHardBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotHardBusy));
            OnPropertyChanged(nameof(IsWorking));
            OnPropertyChanged(nameof(CanChangeInGameSkins));
            NotifyCommands();
        }
    }

    public bool IsNotSoftBusy => !IsSoftBusy;
    public bool IsNotHardBusy => !IsHardBusy;

    public bool CanToggleAutoSync => IsNotHardBusy && _elevatorService.ElevatorStatus == ElevatorStatus.Running;

    public bool IsWorking => IsSoftBusy || IsHardBusy;


    public CharacterDetailsViewModel()
    {
        _busySetter = new BusySetter(this);
    }


    public ModGridVM ModGridVM { get; private set; } = App.GetService<ModGridVM>();
    public ModPaneVM ModPaneVM { get; private set; } = App.GetService<ModPaneVM>();
    public ContextMenuVM ContextMenuVM { get; private set; } = App.GetService<ContextMenuVM>();

    public async void OnNavigatedTo(object parameter)
    {
        _navigationCancellationTokenSource = new CancellationTokenSource();
        CancellationToken = _navigationCancellationTokenSource.Token;

        try
        {
            await InitAsync(parameter, _busySetter.StartHardBusy()).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 正常取消，无需处理
        }
        catch (Exception e)
        {
            ErrorNavigateBack(e);
        }
    }

    private async Task InitAsync(object parameter, BusySetter.CommandTracker commandTracker)
    {
        if (IsReturning) return;

        OnInitializingStarted?.Invoke(this, EventArgs.Empty);

        // Init character card
        InitCharacterCard(parameter);
        LoadingItemText = "模组";

        // Yield to UI
        await Task.Delay(100, CancellationToken);
        if (IsReturning) return;


        // Load mods
        await InitModGridAsync();
        LoadingItemText = "ModPane";

        if (IsReturning) return;

        // Yield to UI
        await Task.Delay(50, CancellationToken);
        commandTracker.Finish();
        if (IsReturning) return;

        // Init Mod Pane
        await InitModPaneAsync();
        LoadingItemText = "Toolbar";
        if (IsReturning) return;

        await InitToolbarAsync();
        LoadingItemText = "Context Menu";

        await InitContextMenuAsync();
        LoadingItemText = "Grid";


        // Wait for the grid to load the datasource
        if (GridLoadedAwaiter is not null)
        {
            try
            {
                await GridLoadedAwaiter();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "GridLoadedAwaiter failed");
            }
        }
        GridLoadedAwaiter = null;

        // Now that the grid is loaded, we can select the first mod
        if (!IsReturning)
        {
            AutoSelectFirstMod();

            // Finished initializing
            IsNavigationFinished = true;
            OnInitializingFinished?.Invoke(this, EventArgs.Empty);
            NotifyCommands();
        }
    }

    private void AutoSelectFirstMod()
    {
        if (IsReturning) return;
        var modToSelect = ModGridVM.GridMods.FirstOrDefault(m => m.IsEnabled) ?? ModGridVM.GridMods.FirstOrDefault();

        if (modToSelect is null)
            return;

        ModGridVM.SetSelectedMod(modToSelect.Id);
    }

    private async Task SetSortOrder()
    {
        try
        {
            var settings = await ReadSettingsAsync();
            if (settings.SortByDescending == null || settings.SortingMethod == null)
                return;

            ModGridVM.SetModSorting(settings.SortingMethod, settings.SortByDescending.Value);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load sort order settings");
        }
    }


    private async Task InitModGridAsync()
    {
        ModGridVM.BusySetter = _busySetter;
        ModGridVM.OnModsReloaded += OnModsReloaded;
        ModGridVM.DeleteModKeyTriggered += ModGridVM_DeleteModKeyTriggered;

        await SetSortOrder();

        await ModGridVM.InitializeAsync(CreateContext(), CancellationToken);

        ModGridVM.OnModsSelected += OnModsSelected;

        if (IsReturning) return;

        ModGridVM.IsBusy = false;
        OnModsLoaded?.Invoke(this, EventArgs.Empty);
    }

    private async Task InitContextMenuAsync()
    {
        await ContextMenuVM.InitializeAsync(CreateContext(), _busySetter, CancellationToken);
        ContextMenuVM.ModsMoved += ContextMenuVM_ModsMoved;
        ContextMenuVM.ModCharactersSkinOverriden += ContextMenuVM_ModsMoved;
    }


    private void ContextMenuVM_ModsMoved(object? sender, EventArgs e)
    {
        ModGridVM.QueueModRefresh();
    }

    private void ModGridVM_DeleteModKeyTriggered(object? sender, EventArgs e)
    {
        if (!DeleteModsCommand.CanExecute(null))
            return;
        DeleteModsCommand.ExecuteAsync(null);
    }

    private void OnModsReloaded(object? sender, EventArgs e) => UpdateTrackedMods();

    private async void OnModsSelected(object? sender, ModGridVM.ModRowSelectedEventArgs args)
    {
        if (IsReturning) return;

        ContextMenuVM.SetSelectedMods(args.Mods.Select(m => m.Id));
        IsSingleModSelected = args.Mods.Count == 1;
        DeleteModsCommand.NotifyCanExecuteChanged();
        var selectedMod = args.Mods.FirstOrDefault();

        ModPaneVM.QueueLoadMod(selectedMod?.Id);

        if (selectedMod is null)
            return;

        var recentlyAddedModNotifications = args.Mods.SelectMany(x =>
            x.ModNotifications.Where(notification => notification.AttentionType == AttentionType.Added)).ToArray();

        if (recentlyAddedModNotifications.Length == 0) return;


        foreach (var modNotification in recentlyAddedModNotifications)
        {
            await _modNotificationManager.RemoveModNotificationAsync(modNotification.Id);

            foreach (var newModModel in args.Mods)
            {
                var notification = newModModel.ModNotifications.FirstOrDefault(x => x.Id == modNotification.Id);
                if (notification is not null) newModModel.ModNotifications.Remove(notification);
            }
        }
    }

    private async Task InitModPaneAsync()
    {
        ModPaneVM.BusySetter = _busySetter;
        // 传递 DispatcherQueue 以确保 UI 更新在正确线程
        await ModPaneVM.OnNavigatedToAsync(DispatcherQueue.GetForCurrentThread(), CancellationToken);
    }

    public async void OnNavigatedFrom()
    {
        try
        {
            if (_navigationCancellationTokenSource != null)
            {
                await _navigationCancellationTokenSource.CancelAsync();
            }

            ModGridVM.OnModsSelected -= OnModsSelected;
            ModGridVM.OnModsReloaded -= OnModsReloaded;
            ContextMenuVM.ModsMoved -= ContextMenuVM_ModsMoved;
            ContextMenuVM.ModCharactersSkinOverriden -= ContextMenuVM_ModsMoved;
            ModGridVM.DeleteModKeyTriggered -= ModGridVM_DeleteModKeyTriggered;

            if (ModGridVM.IsInitialized)
                ModGridVM.OnNavigateFrom();
            if (ModPaneVM.IsInitialized)
                ModPaneVM.OnNavigatedFrom();

            var sortingMethod = ModGridVM.CurrentSortingMethod.SortingMethodType;
            var isDescending = ModGridVM.IsDescendingSort;

            _ = Task.Run(async () =>
            {
                try
                {
                    var settings = await _localSettingsService.ReadOrCreateSettingAsync<CharacterDetailsSettings>(CharacterDetailsSettings.Key, SettingScope.App);
                    settings.SortingMethod = sortingMethod;
                    settings.SortByDescending = isDescending;
                    await _localSettingsService.SaveSettingAsync(CharacterDetailsSettings.Key, settings, SettingScope.App);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to save settings on navigation from CharacterDetails");
                }
            });

            _navigationCancellationTokenSource?.Dispose();
            _navigationCancellationTokenSource = null;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Error while navigating from CharacterDetailsViewModel");
#if DEBUG
            Debugger.Break();
#endif
        }
    }


    private InternalName? ParseNavigationArg(object parameter)
    {
        return parameter switch
        {
            CharacterGridItemModel characterGridItemModel => characterGridItemModel.Character.InternalName,
            INameable iInternalName => iInternalName.InternalName,
            string internalNameString => new InternalName(internalNameString),
            InternalName internalName1 => internalName1,
            _ => null
        };
    }

    private void ErrorNavigateBack(Exception? exception = null)
    {
        if (_isErrorNavigateBack)
            return;
        _isErrorNavigateBack = true;

        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (exception is not null)
                _notificationService.ShowNotification("An error occurred while loading the character details page.",
                    exception.Message, null);
            else
                _notificationService.ShowNotification("An error occurred while loading the character details page.", "",
                    null);

            if (_navigationService.CanGoBack)
                _navigationService.GoBack();
            else
                _navigationService.NavigateTo(typeof(CharactersViewModel).FullName!);
        });
    }

    [RelayCommand]
    private async Task GoToGalleryScreen()
    {
        var settings = await _localSettingsService.ReadOrCreateSettingAsync<CharacterDetailsSettings>(
            CharacterDetailsSettings.Key, SettingScope.App);

        settings.GalleryView = true;

        await _localSettingsService.SaveSettingAsync(CharacterDetailsSettings.Key, settings, SettingScope.App);

        _navigationService.NavigateTo(typeof(CharacterGalleryViewModel).FullName!, ShownModObject.InternalName);
        _navigationService.ClearBackStack(1);
    }

    private Task<CharacterDetailsSettings> ReadSettingsAsync() =>
        _localSettingsService.ReadOrCreateSettingAsync<CharacterDetailsSettings>(CharacterDetailsSettings.Key,
            SettingScope.App);

    private Task SaveSettingsAsync(CharacterDetailsSettings settings) =>
        _localSettingsService.SaveSettingAsync(CharacterDetailsSettings.Key, settings, SettingScope.App);


    private async Task CommandWrapperAsync(bool startHardBusy, Func<Task> command)
    {
        using var _ = startHardBusy ? _busySetter.StartHardBusy() : _busySetter.StartSoftBusy();
        try
        {
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
            _notificationService.ShowNotification("An unknown error occured while executing the command", e.Message,
                TimeSpan.FromSeconds(5));
        }
    }

    private IRelayCommand[]? _viewModelCommands;

    private void NotifyCommands()
    {
        if (_viewModelCommands is null)
        {
            var commands = new List<IRelayCommand>();
            foreach (var propertyInfo in GetType()
                         .GetProperties()
                         .Where(p => p.PropertyType.IsAssignableTo(typeof(IRelayCommand))))
            {
                var value = propertyInfo.GetValue(this);

                if (value is IRelayCommand relayCommand)
                    commands.Add(relayCommand);
            }

            _viewModelCommands = commands.ToArray();
        }

        _viewModelCommands.ForEach(c => c.NotifyCanExecuteChanged());
    }
}

public partial class BusySetter(CharacterDetailsViewModel viewModel) : ObservableObject
{
    private readonly CharacterDetailsViewModel _viewModel = viewModel;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSoftBusy), nameof(IsWorking))]
    private bool _isSoftBusy; // App is doing something, but the user can still do other things

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotHardBusy), nameof(IsWorking))]
    private bool _isHardBusy; // App is doing something, and the user can't do anything on the page

    public bool IsNotSoftBusy => !IsSoftBusy;
    public bool IsNotHardBusy => !IsHardBusy;

    public bool IsWorking => IsSoftBusy || IsHardBusy;

    public event EventHandler? SoftBusyChanged;
    public event EventHandler? HardBusyChanged;


    private readonly ConcurrentDictionary<Guid, byte> _trackedSoftCommands = [];
    private readonly ConcurrentDictionary<Guid, byte> _trackedHardCommands = [];

    private void Refresh()
    {
        var oldSoftBusy = IsSoftBusy;
        var oldHardBusy = IsHardBusy;

        IsSoftBusy = !_trackedSoftCommands.IsEmpty;
        IsHardBusy = !_trackedHardCommands.IsEmpty;

        if (_viewModel != null)
        {
            _viewModel.IsHardBusy = IsHardBusy;
            _viewModel.IsSoftBusy = IsSoftBusy;
        }

        if (oldSoftBusy != IsSoftBusy)
            SoftBusyChanged?.Invoke(this, EventArgs.Empty);

        if (oldHardBusy != IsHardBusy)
            HardBusyChanged?.Invoke(this, EventArgs.Empty);
    }

    public CommandTracker StartSoftBusy()
    {
        var tracker = new CommandTracker(this, false);
        _trackedSoftCommands.TryAdd(tracker.CommandId, 0);
        Refresh();
        return tracker;
    }

    public CommandTracker StartHardBusy()
    {
        var tracker = new CommandTracker(this, true);
        _trackedHardCommands.TryAdd(tracker.CommandId, 0);
        Refresh();
        return tracker;
    }


    public readonly struct CommandTracker(BusySetter busySetter, bool isHardBusy) : IDisposable
    {
        public readonly Guid CommandId = Guid.NewGuid();

        public void Dispose() => Finish();

        public void Finish()
        {
            if (isHardBusy)
                busySetter._trackedHardCommands.TryRemove(CommandId, out _);
            else
                busySetter._trackedSoftCommands.TryRemove(CommandId, out _);

            busySetter.Refresh();
        }
    }
}