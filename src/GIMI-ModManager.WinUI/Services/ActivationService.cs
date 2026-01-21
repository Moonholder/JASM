using System.Security.Principal;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using CommunityToolkitWrapper;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.Activation;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Options;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Services.AppManagement.Updating;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.Views;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace GIMI_ModManager.WinUI.Services;

public class ActivationService : IActivationService
{
    private readonly NotificationManager _notificationManager;
    private readonly ISkinManagerService _skinManagerService;
    private readonly INavigationViewService _navigationViewService;
    private readonly ActivationHandler<LaunchActivatedEventArgs> _defaultHandler;
    private readonly IEnumerable<IActivationHandler> _activationHandlers;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILogger _logger;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IGameService _gameService;
    private readonly ILanguageLocalizer _languageLocalizer;
    private readonly ElevatorService _elevatorService;
    private readonly GenshinProcessManager _genshinProcessManager;
    private readonly ThreeDMigtoProcessManager _threeDMigtoProcessManager;
    private readonly UpdateChecker _updateChecker;
    private readonly IWindowManagerService _windowManagerService;
    private readonly AutoUpdaterService _autoUpdaterService;
    private readonly SelectedGameService _selectedGameService;
    private readonly ModUpdateAvailableChecker _modUpdateAvailableChecker;
    private readonly ModNotificationManager _modNotificationManager;
    private readonly LifeCycleService _lifeCycleService;
    private UIElement? _shell = null;

    private readonly string[] _args = Environment.GetCommandLineArgs().Skip(1).ToArray();

    public ActivationService(ActivationHandler<LaunchActivatedEventArgs> defaultHandler,
        IEnumerable<IActivationHandler> activationHandlers, IThemeSelectorService themeSelectorService,
        ILocalSettingsService localSettingsService,
        ElevatorService elevatorService, GenshinProcessManager genshinProcessManager,
        ThreeDMigtoProcessManager threeDMigtoProcessManager, UpdateChecker updateChecker,
        IWindowManagerService windowManagerService, AutoUpdaterService autoUpdaterService, IGameService gameService,
        ILanguageLocalizer languageLocalizer, SelectedGameService selectedGameService,
        ModUpdateAvailableChecker modUpdateAvailableChecker, ILogger logger,
        ModNotificationManager modNotificationManager, INavigationViewService navigationViewService,
        ISkinManagerService skinManagerService, NotificationManager notificationManager,
        LifeCycleService lifeCycleService)
    {
        _defaultHandler = defaultHandler;
        _activationHandlers = activationHandlers;
        _themeSelectorService = themeSelectorService;
        _localSettingsService = localSettingsService;
        _elevatorService = elevatorService;
        _genshinProcessManager = genshinProcessManager;
        _threeDMigtoProcessManager = threeDMigtoProcessManager;
        _updateChecker = updateChecker;
        _windowManagerService = windowManagerService;
        _autoUpdaterService = autoUpdaterService;
        _gameService = gameService;
        _languageLocalizer = languageLocalizer;
        _selectedGameService = selectedGameService;
        _modUpdateAvailableChecker = modUpdateAvailableChecker;
        _modNotificationManager = modNotificationManager;
        _navigationViewService = navigationViewService;
        _skinManagerService = skinManagerService;
        _notificationManager = notificationManager;
        _lifeCycleService = lifeCycleService;
        _logger = logger.ForContext<ActivationService>();
    }

    public async Task ActivateAsync(object activationArgs)
    {
#if DEBUG
        _logger.Information("JASM starting up in DEBUG mode...");
#elif RELEASE
        _logger.Information("JASM starting up in RELEASE mode...");
#endif

        await HandleLaunchArgsAsync();

        // Check if there is another instance of JASM running
        await CheckIfAlreadyRunningAsync();

        // Execute tasks before activation.
        await InitializeAsync();

        // Set the MainWindow Content.
        if (App.MainWindow.Content == null)
        {
            _shell = App.GetService<ShellPage>();
            App.MainWindow.Content = _shell ?? new Frame();
        }

        // Handle activation via ActivationHandlers.
        await HandleActivationAsync(activationArgs);

        // Activate the MainWindow.
        App.MainWindow.Activate();

        // Set MainWindow Cleanup on Close.
        App.MainWindow.Closed += OnApplicationExit;

        // Execute tasks after activation.
        await StartupAsync();

        // Show popups
        ShowStartupPopups();
    }

    private async Task CheckIfAlreadyRunningAsync()
    {
        var isJasmRunningHWND = await IsJasmRunning();

        if (!isJasmRunningHWND.HasValue) return;

        var hWnd = isJasmRunningHWND.Value;

        _logger.Information("JASM is already running, exiting...");
        try
        {
            PInvoke.ShowWindow(hWnd, SHOW_WINDOW_CMD.SW_RESTORE);
            PInvoke.SetWindowPos(hWnd, new HWND(IntPtr.Zero), 0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
            PInvoke.SetForegroundWindow(hWnd);
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not bring JASM to foreground");
            return;
        }

        Application.Current.Exit();
        await Task.Delay(-1);
    }


    private async Task<HWND?> IsJasmRunning()
    {
        nint? processHandle;
        try
        {
            processHandle = await _lifeCycleService.CheckIfAlreadyRunningAsync();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Could not determine if JASM is already running. Assuming not");
            return null;
        }

        if (processHandle == null) return null;

        return new HWND(processHandle.Value);
    }

    private async Task HandleActivationAsync(object activationArgs)
    {
        var activationHandler = _activationHandlers.FirstOrDefault(h => h.CanHandle(activationArgs));


        if (activationHandler is not null)
        {
            _logger.Debug("Handling activation: {ActivationName}",
                activationHandler?.ActivationName);

            await activationHandler?.HandleAsync(activationArgs)!;
        }

        if (_defaultHandler.CanHandle(activationArgs))
        {
            _logger.Debug("Handling activation: {ActivationName}", _defaultHandler.ActivationName);
            await _defaultHandler.HandleAsync(activationArgs);
        }
    }

    private async Task InitializeAsync()
    {
        await _selectedGameService.InitializeAsync();
        await SetLanguage();
        await SetWindowSettings();
        await _themeSelectorService.InitializeAsync().ConfigureAwait(false);
        _notificationManager.Initialize();
    }


    private async Task StartupAsync()
    {
        await _themeSelectorService.SetRequestedThemeAsync();
        await _genshinProcessManager.TryInitialize();
        await _threeDMigtoProcessManager.TryInitialize();
        await _updateChecker.InitializeAsync();
        await _modUpdateAvailableChecker.InitializeAsync().ConfigureAwait(false);
        await Task.Run(() => _autoUpdaterService.UpdateAutoUpdater()).ConfigureAwait(false);
        await Task.Run(() => _elevatorService.Initialize()).ConfigureAwait(false);
    }

    const int MinimizedPosition = -32000;

    private async Task SetWindowSettings()
    {
        var screenSize = await _localSettingsService.ReadSettingAsync<ScreenSizeSettings>(ScreenSizeSettings.Key);
        if (screenSize == null)
            return;

        if (screenSize.PersistWindowSize && screenSize.Width != 0 && screenSize.Height != 0)
        {
            _logger.Debug($"Window size loaded: {screenSize.Width}x{screenSize.Height}");
            App.MainWindow.SetWindowSize(screenSize.Width, screenSize.Height);
        }

        if (screenSize.PersistWindowPosition)
        {
            if (screenSize.XPosition != 0 && screenSize.YPosition != 0 &&
                screenSize.XPosition != MinimizedPosition && screenSize.YPosition != MinimizedPosition)
                App.MainWindow.AppWindow.Move(new PointInt32(screenSize.XPosition, screenSize.YPosition));
            else
                App.MainWindow.CenterOnScreen();

            if (screenSize.IsFullScreen)
                App.MainWindow.Maximize();
        }
    }

    private async void OnApplicationExit(object sender, WindowEventArgs args)
    {
        if (App.ShutdownComplete) return;

        args.Handled = true;

        if (App.IsShuttingDown)
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(async () =>
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            {
                var softShutdownGracePeriod = TimeSpan.FromSeconds(2);
                await Task.Delay(softShutdownGracePeriod);

                _logger.Warning(
                    "JASM shutdown took too long (>{maxShutdownGracePeriod}s), ignoring cleanup and exiting...",
                    softShutdownGracePeriod);
                App.ShutdownComplete = true;
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    Application.Current.Exit();
                    App.MainWindow.Close();
                });
            });

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(async () =>
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            {
                var maxShutdownGracePeriod = TimeSpan.FromSeconds(5);
                await Task.Delay(maxShutdownGracePeriod);

                _logger.Fatal("JASM failed to close after {maxShutdownGracePeriod} seconds, forcing exit...",
                    maxShutdownGracePeriod);
                Environment.Exit(1);
            });
            return;
        }


        await _lifeCycleService.StartShutdownAsync().ConfigureAwait(false);
    }

    // Declared here for now, might move to a different class later.
    private const string IgnoreAdminWarningKey = "IgnoreAdminPrivelegesWarning";

    private async Task HandleLaunchArgsAsync()
    {
        if (_args.Length == 0)
            return;

        var supportedGames = Enum.GetNames<SupportedGames>().Select(v => v.ToLower()).ToArray();

        if (_args.Any(arg => arg.Contains("help", StringComparison.OrdinalIgnoreCase)) &&
            // WinUI doesnt seem to launch like a regular console app.
            // This kinda works, but it's not perfect.
            // Overriding the main entry point didn't seem to work either.
            PInvoke.AttachConsole(unchecked((uint)-1)))
        {
            // TODO: Use CommandLineParser or something similar.
            Console.WriteLine("JASM Command line arguments:");

            Console.WriteLine(
                $"      --game <game> - Launch JASM with the specified game selected. Supported games: {string.Join('|', supportedGames)}");

            Console.WriteLine(
                "           --switch - If used with --game, will switch to the selected game if JASM is already running. This is done by exiting the already running instance.");

            Application.Current.Exit();
            await Task.Delay(-1);
        }


        await _selectedGameService.InitializeAsync().ConfigureAwait(false);
        var notSelectedGames = await _selectedGameService.GetNotSelectedGameAsync().ConfigureAwait(false);

        var launchGameArgIndex =
            Array.FindIndex(_args, arg => arg.Equals("--game", StringComparison.OrdinalIgnoreCase));
        if (launchGameArgIndex == -1)
            return;

        var launchGameArgValue = _args.ElementAtOrDefault(launchGameArgIndex + 1);
        if (launchGameArgValue.IsNullOrEmpty())
        {
            _logger.Warning("No game specified for arg: --game <{ValidGames}>",
                string.Join('|', Enum.GetNames<SupportedGames>().Select(v => v.ToLower())));
            return;
        }

        var selectedGame = Enum.TryParse<SupportedGames>(launchGameArgValue, true, out var game)
            ? game
            : SupportedGames.Genshin;

        if (!notSelectedGames.Contains(selectedGame))
            return;

        var otherProcess = _lifeCycleService.GetOtherInstanceProcess();

        if (otherProcess is not null)
        {
            if (!_args.Contains("--switch"))
            {
                // If the other instance is running, and the switch flag is not present, return.
                // Will be handled by the OtherInstance check later.
                return;
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                _logger.Information("Closing running instance of JASM to switch to {SelectedGame}", selectedGame);
                otherProcess.CloseMainWindow();
                await otherProcess.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to close running instance of JASM");
                return;
            }

            try
            {
                await _selectedGameService.SaveSelectedGameAsync(selectedGame.ToString()).ConfigureAwait(false);
                await _selectedGameService.InitializeAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // If this errors then I don't know what to do.
                _logger.Error(e, "Failed to save selected game");
            }

            return;
        }

        try
        {
            await _selectedGameService.SaveSelectedGameAsync(selectedGame.ToString()).ConfigureAwait(false);
            await _selectedGameService.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // If this errors then I don't know what to do.
            _logger.Error(e, "Failed to save selected game");
            return;
        }


        _logger.Information("Game selected via launch args: {SelectedGame}", selectedGame);
    }

    private void ShowStartupPopups()
    {
        App.MainWindow.DispatcherQueue.EnqueueAsync(async () =>
        {
            await Task.Delay(2000);
            await AdminWarningPopup();
            await Task.Delay(1000);
            await NewFolderStructurePopup();
        });
    }

    private async Task AdminWarningPopup()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        if (!principal.IsInRole(WindowsBuiltInRole.Administrator)) return;

        var ignoreWarning = await _localSettingsService.ReadSettingAsync<bool>(IgnoreAdminWarningKey);

        if (ignoreWarning) return;

        var stackPanel = new StackPanel();
        var textWarning = new TextBlock()
        {
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/Startup/AdminWarning_Text",
                   "You are running JASM as Administrator. This is NOT recommended.\n" +
                   "JASM is not designed to be run with elevated privileges.\n" +
                   "While unlikely, simple bugs could potentially cause serious damage to your file system.\n\n" +
                   "Drag and Drop functionality is disabled under Administrator privileges due to UIPI isolation.\n" +
                   "Please consider running JASM without Administrator privileges.\n\n" +
                   "Use at your own risk, you have been warned."),
            TextWrapping = TextWrapping.WrapWholeWords
        };
        stackPanel.Children.Add(textWarning);

        var doNotShowAgain = new CheckBox()
        {
            IsChecked = false,
            Content = _languageLocalizer.GetLocalizedStringOrDefault("/Startup/AdminWarning_DoNotShowAgain", "Do not show this warning again"),
            Margin = new Thickness(0, 10, 0, 0)
        };

        stackPanel.Children.Add(doNotShowAgain);


        var dialog = new ContentDialog
        {
            Title = _languageLocalizer.GetLocalizedStringOrDefault("/Startup/AdminWarning_Title", "Run as Administrator Warning"),
            Content = stackPanel,
            PrimaryButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/Startup/AdminWarning_ConfirmButton", "I understand"),
            SecondaryButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/Startup/AdminWarning_ExitButton", "Exit"),
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await _windowManagerService.ShowDialogAsync(dialog);

        if (result == ContentDialogResult.Secondary) Application.Current.Exit();

        if (doNotShowAgain.IsChecked == true)
            await _localSettingsService.SaveSettingAsync(IgnoreAdminWarningKey, true);
    }

    public const string IgnoreNewFolderStructureKey = "IgnoreNewFolderStructureWarning";

    private async Task NewFolderStructurePopup()
    {
        if (!_skinManagerService.IsInitialized)
        {
            await _localSettingsService.SaveSettingAsync(IgnoreNewFolderStructureKey, true);
        }

        var ignoreWarning = await _localSettingsService.ReadOrCreateSettingAsync<bool>(IgnoreNewFolderStructureKey);

        if (ignoreWarning) return;

        var stackPanel = new StackPanel();
        var textWarning = new TextBlock()
        {
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/Startup/NewFolderStructure_Text1",
                   """
                   This version of JASM adopts a new folder structure.

                   Characters are now organized by category, each with its own folder. So the new format is:
                   Mods/Category/Character/<Mod Folder>
                   Therefore, JASM will not recognize any of your mods until you reorganize them.
                   This is a one-time operation, you can do it manually if you wish.

                   Additionally, character folders are now created on demand and empty folders can be cleaned up in the settings page.
                   """),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        stackPanel.Children.Add(textWarning);

        var textWarning2 = new TextBlock()
        {
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/Startup/NewFolderStructure_Text2", "If you are unsure about this, please backup your mods first. I have tested this on my own mods."),
            FontWeight = FontWeights.Bold,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        stackPanel.Children.Add(textWarning2);


        var textWarning3 = new TextBlock()
        {
            Text = _languageLocalizer.GetLocalizedStringOrDefault("/Startup/NewFolderStructure_Text3",
                   """

                   This popup will persist until you choose one of the options below. You can also use the "Reorganize Mods" button on the settings page.
                   If you want to see what is happening, check the logs.
                   """),
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        stackPanel.Children.Add(textWarning3);


        var dialog = new ContentDialog
        {
            Title = _languageLocalizer.GetLocalizedStringOrDefault("/Startup/NewFolderStructure_Title", "New Folder Structure"),
            Content = stackPanel,
            PrimaryButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/Startup/NewFolderStructure_ReorganizeButton", "Reorganize my mods"),
            SecondaryButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/Startup/NewFolderStructure_ManualButton", "I'll do it myself"),
            CloseButtonText = _languageLocalizer.GetLocalizedStringOrDefault("/Startup/NewFolderStructure_CancelButton", "Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await _windowManagerService.ShowDialogAsync(dialog);

        if (result == ContentDialogResult.Primary)
        {
            _navigationViewService.IsEnabled = false;

            try
            {
                var movedModsCount = await Task.Run(() =>
                    _skinManagerService.ReorganizeModsAsync()); // Mods folder

                await _skinManagerService.RefreshModsAsync();

                if (movedModsCount == -1)
                    _notificationManager.ShowNotification(
                        _languageLocalizer.GetLocalizedStringOrDefault("/Startup/NewFolderStructure_ReorganizeFailedTitle", "Mod reorganization failed."),
                        _languageLocalizer.GetLocalizedStringOrDefault("/Startup/NewFolderStructure_SeeLogs", "See logs for details."),
                        TimeSpan.FromSeconds(5));

                else
                    _notificationManager.ShowNotification(
                        _languageLocalizer.GetLocalizedStringOrDefault("/Startup/NewFolderStructure_ReorganizeSuccessTitle", "Mods reorganized."),
                        string.Format(_languageLocalizer.GetLocalizedStringOrDefault("/Startup/NewFolderStructure_ReorganizeSuccessMessage", "Moved {0} mods to new character folders"), movedModsCount),
                        TimeSpan.FromSeconds(5));
            }
            finally
            {
                _navigationViewService.IsEnabled = true;
                await _localSettingsService.SaveSettingAsync(IgnoreNewFolderStructureKey, true);
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            await _localSettingsService.SaveSettingAsync(IgnoreNewFolderStructureKey, true);
        }
        else
        {
        }
    }


    private async Task SetLanguage()
    {
        var selectedLanguage = (await _localSettingsService.ReadOrCreateSettingAsync<AppSettings>(AppSettings.Key))
            .Language?.ToLower().Trim();
        if (selectedLanguage == null)
        {
            return;
        }

        var supportedLanguages = _languageLocalizer.AvailableLanguages;
        var language = supportedLanguages.FirstOrDefault(lang =>
            lang.LanguageCode.Equals(selectedLanguage, StringComparison.CurrentCultureIgnoreCase));

        if (language != null)
            await _languageLocalizer.SetLanguageAsync(language);
    }
}