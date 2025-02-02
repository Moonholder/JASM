﻿using System.Security.Principal;
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
            Text = "您正在以管理员身份运行 JASM。这不推荐这样做.\n" +
                   "JASM 并非设计为在管理员权限下运行.\n" +
                   "尽管不太可能，但简单的漏洞有可能会对您的文件系统造成严重损害.\n\n" +
                   "请考虑在无管理员权限的情况下运行 JASM.\n\n" +
                   "自行承担使用风险，已对您做出警告",
            TextWrapping = TextWrapping.WrapWholeWords
        };
        stackPanel.Children.Add(textWarning);

        var doNotShowAgain = new CheckBox()
        {
            IsChecked = false,
            Content = "不再显示此警告",
            Margin = new Thickness(0, 10, 0, 0)
        };

        stackPanel.Children.Add(doNotShowAgain);


        var dialog = new ContentDialog
        {
            Title = "以管理员身份运行警告",
            Content = stackPanel,
            PrimaryButtonText = "我知道了",
            SecondaryButtonText = "退出",
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
            Text = """
                   这个版本的 JASM 采用了新的文件夹结构.

                   现在，角色按类别进行组织，每个类别都有其自己的文件夹。因此，新的格式如下:
                   Mods/类别/角色/<模组文件夹>
                   所以，在您重新整理模组之前，JASM 将无法识别您的任何模组.
                   这是一次性的操作，如果您愿意的话，可以手动进行整理.

                   此外，现在角色文件夹会按需创建，并且可以在设置页面清理空文件夹.
                   """,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        };
        stackPanel.Children.Add(textWarning);

        var textWarning2 = new TextBlock()
        {
            Text = "如果你对此不确定，那么请先备份你的模组。我已经在自己的模组上进行过测试了.",
            FontWeight = FontWeights.Bold,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        stackPanel.Children.Add(textWarning2);


        var textWarning3 = new TextBlock()
        {
            Text = """

                   在你选择以下某个选项之前，这个弹出窗口将会一直显示。你也可以使用设置页面上的 “重新整理” 按钮.
                   如果你想了解正在发生什么情况，可以查看日志.
                   """,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.WrapWholeWords
        };

        stackPanel.Children.Add(textWarning3);


        var dialog = new ContentDialog
        {
            Title = "新文件夹结构",
            Content = stackPanel,
            PrimaryButtonText = "重新整理我的模组",
            SecondaryButtonText = "我自己来做",
            CloseButtonText = "取消",
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
                    _notificationManager.ShowNotification("模组重新整理失败.",
                        "有关详细信息，请参阅日志.", TimeSpan.FromSeconds(5));

                else
                    _notificationManager.ShowNotification("模组已重新整理.",
                        $"已将 {movedModsCount} 个模组移至新的角色文件夹", TimeSpan.FromSeconds(5));
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