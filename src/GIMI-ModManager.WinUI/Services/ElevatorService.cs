using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.WinUI.UI.Controls;
using CommunityToolkitWrapper;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.Helpers;
using Microsoft.UI.Xaml;
using Serilog;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

namespace GIMI_ModManager.WinUI.Services;

public partial class ElevatorService : ObservableRecipient
{
    private readonly ISkinManagerService _skinManagerService;
    private readonly IGameService _gameService;
    public const string ElevatorPipeName = "MyPipess";
    public const string ElevatorProcessName = "Elevator.exe";
    private readonly ILogger _logger;
    private Task? _refreshTask;
    private readonly object _refreshLock = new();

    private ElevatorStatus _elevatorStatus = ElevatorStatus.NotRunning;

    public ElevatorStatus ElevatorStatus
    {
        get => _elevatorStatus;
        set
        {
            if (SetProperty(ref _elevatorStatus, value))
            {
                OnPropertyChanged(nameof(ElevatorStatusText));
            }
        }
    }

    public string ElevatorStatusText => ElevatorStatus switch
    {
        ElevatorStatus.InitializingFailed => "初始化失败",
        ElevatorStatus.NotRunning => "未运行",
        ElevatorStatus.Running => "运行中",
        _ => "未知"
    };

    [ObservableProperty] private bool _canStartElevator;
    private Process? _elevatorProcess;

    public string? ErrorMessage { get; private set; }

    private bool _exitHandlerRegistered;

    private bool _IsInitialized;

    public ElevatorService(ILogger logger, ISkinManagerService skinManagerService, IGameService gameService)
    {
        _skinManagerService = skinManagerService;
        _gameService = gameService;
        _logger = logger.ForContext<ElevatorService>();
    }

    public void Initialize()
    {
        if (_IsInitialized) throw new InvalidOperationException("ElevatorService is already initialized");
        _logger.Debug("Initializing ElevatorService");
        var elevatorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ElevatorProcessName);
        if (Path.Exists(elevatorPath))
        {
            _logger.Debug(ElevatorProcessName + " found at: " + elevatorPath);
            App.MainWindow.DispatcherQueue.TryEnqueue(() => CanStartElevator = true);
            if (!_exitHandlerRegistered)
            {
                App.MainWindow.Closed += MainWindowExitHandler;
                _exitHandlerRegistered = true;
            }

            _IsInitialized = true;
            return;
        }

        _logger.Warning("Elevator.exe not found");
        ErrorMessage = "Elevator.exe not found";
        ElevatorStatus = ElevatorStatus.InitializingFailed;
        App.MainWindow.DispatcherQueue.TryEnqueue(() => CanStartElevator = false);
    }

    public bool StartElevator()
    {
        var running = AttachToExistingElevatorProcess();
        if (running)
        {
            _logger.Information("Attached to existing Elevator.exe process");
            App.MainWindow.DispatcherQueue.TryEnqueue(() => ElevatorStatus = ElevatorStatus.Running);
            App.MainWindow.DispatcherQueue.TryEnqueue(() => CanStartElevator = false);
            return true;
        }

        var currentUser = WindowsIdentity.GetCurrent().Name;
        currentUser = currentUser.Split("\\").LastOrDefault() ?? currentUser;

        _elevatorProcess = Process.Start(new ProcessStartInfo(ElevatorProcessName)
        {
            UseShellExecute = true,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            Verb = "runas",
            ArgumentList = { currentUser }
        });

        if (_elevatorProcess == null || _elevatorProcess.HasExited)
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() => ElevatorStatus = ElevatorStatus.InitializingFailed);
            ErrorMessage = "Failed to start Elevator.exe";
            _logger.Error("Failed to start Elevator.exe");
            return false;
        }

        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            ElevatorStatus = ElevatorStatus.Running;
            CanStartElevator = false;
        });

        _elevatorProcess.Exited += (sender, args) =>
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                ElevatorStatus = ElevatorStatus.NotRunning;
                CanStartElevator = true;
            });
            _logger.Information("Elevator.exe exited with exit code: {ExitCode}", _elevatorProcess.ExitCode);
        };

        App.MainWindow.DispatcherQueue.TryEnqueue(() => CanStartElevator = false);

        return true;
    }

    private bool AttachToExistingElevatorProcess()
    {
        try
        {
            var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ElevatorProcessName));
            var proc = procs.FirstOrDefault(p => !p.HasExited);
            if (proc != null)
            {
                _elevatorProcess = proc;
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to attach to existing Elevator.exe process");
        }

        return false;
    }


    private void MainWindowExitHandler(object sender, WindowEventArgs args)
    {
        if (_elevatorProcess is { HasExited: false })
        {
            _logger.Information("Killing Elevator.exe");
            _elevatorProcess.Kill();
            _logger.Debug("Elevator.exe killed");
        }
    }

    public Task RefreshGenshinMods()
    {
        if (_elevatorProcess is null || _elevatorProcess.HasExited)
        {
            _logger.Debug("Elevator.exe is not running");
            return Task.CompletedTask;
        }

        lock (_refreshLock)
        {
            if (_refreshTask is { IsCompleted: false })
            {
                return _refreshTask;
            }

            _refreshTask = Task.Run(async () =>
            {
                await InternalRefreshGenshinMods().ConfigureAwait(false);
                await Task.Delay(500).ConfigureAwait(false); // Debounce
            });

            return _refreshTask;
        }
    }

    private FileSystemWatcher? _userIniWatcher;

    private TaskCompletionSource<bool>? _userIniChangedTcs;

    public async Task RefreshAndWaitForUserIniChangesAsync()
    {
        var userIniPath = Path.Combine(_skinManagerService.ThreeMigotoRootfolder, Constants.UserIniFileName);
        if (!Directory.Exists(_skinManagerService.ThreeMigotoRootfolder) || !File.Exists(userIniPath))
        {
            await RefreshGenshinMods().ConfigureAwait(false);
            return;
        }

        _userIniChangedTcs = new TaskCompletionSource<bool>();

        if (_userIniWatcher is null)
        {
            _userIniWatcher ??= new FileSystemWatcher
            {
                Path = _skinManagerService.ThreeMigotoRootfolder,
                Filter = Constants.UserIniFileName,
                NotifyFilter = NotifyFilters.LastWrite
            };

            _userIniWatcher.Changed += (sender, args) =>
            {
                if (_userIniChangedTcs is { Task: { IsCompleted: false } })
                    _userIniChangedTcs.TrySetResult(true);
            };
        }

        try
        {
            _userIniWatcher.EnableRaisingEvents = true;

            // Create timeout
            _ = Task.Delay(5000).ContinueWith(task =>
            {
                if (_userIniChangedTcs is { Task: { IsCompleted: false } })
                    _userIniChangedTcs.TrySetResult(true);
            });


            await RefreshGenshinMods().ConfigureAwait(false);

            await _userIniChangedTcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _userIniWatcher.EnableRaisingEvents = false;
        }
    }

    private async Task InternalRefreshGenshinMods()
    {
        try
        {
            await using var pipeClient = new NamedPipeClientStream(".", ElevatorPipeName, PipeDirection.Out);
            await pipeClient.ConnectAsync(TimeSpan.FromSeconds(5), default);
            await using var writer = new StreamWriter(pipeClient);
            _logger.Debug("Sending command: {Command}", nameof(InternalRefreshGenshinMods));
            var command = "0:" + _gameService.GameShortName;
            await writer.WriteLineAsync(command);
            await writer.FlushAsync();
            _logger.Debug("Done");
            App.MainWindow.DispatcherQueue.EnqueueAsync(async () =>
            {
                await Task.Delay(500);
                App.MainWindow.SetForegroundWindow();
                App.MainWindow.Activate();
            });
        }
        catch (TimeoutException e)
        {
            _logger.Error(e, "Failed to Refresh Genshin Mods");
        }
    }

    public ElevatorStatus CheckStatus()
    {
        // 优先查找并附加已存在Elevator.exe进程
        if (_elevatorProcess is null || _elevatorProcess.HasExited)
        {
            AttachToExistingElevatorProcess();
        }

        ElevatorStatus = _elevatorProcess is { HasExited: false } ? ElevatorStatus.Running : ElevatorStatus.NotRunning;
        return ElevatorStatus;
    }
}

public enum ElevatorStatus
{
    InitializingFailed = -1,
    NotRunning = 0,
    Running
}