using GIMI_ModManager.Core.Contracts.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using GIMI_ModManager.Core.Services.CommandService;
using GIMI_ModManager.Core.Services.CommandService.Models;
using GIMI_ModManager.WinUI.Models.Options;
using GIMI_ModManager.WinUI.Services.Notifications;
using Serilog;

namespace GIMI_ModManager.WinUI.Services;

// This is an old class that was its own thing at some point. Now that CommandService is a thing, this class is just a wrapper for CommandService
public abstract partial class BaseProcessManager<TProcessOptions> : ObservableObject, IProcessManager
    where TProcessOptions : ProcessOptionsBase, new()
{
    private readonly CommandService _commandService;
    private readonly CommandHandlerService _commandHandler;
    private readonly NotificationManager _notificationManager;
    private readonly ILanguageLocalizer _localizer;
    private CommandDefinition? _commandDefinition;
    private ProcessCommand? _process;


    private readonly ILogger _logger;


    public string ProcessName { get; protected set; } = string.Empty;

    [ObservableProperty] private string? _processPath;


    [ObservableProperty] private ProcessStatus _processStatus = ProcessStatus.NotInitialized;

    public bool IsGameProcessManager => GetType() == typeof(GenshinProcessManager);

    protected BaseProcessManager(ILogger logger)
    {
        _logger = logger;
        _commandService = App.GetService<CommandService>();
        _commandHandler = App.GetService<CommandHandlerService>();
        _notificationManager = App.GetService<NotificationManager>();
        _localizer = App.GetService<ILanguageLocalizer>();
    }


    [MemberNotNullWhen(true, nameof(_commandDefinition), nameof(ProcessPath))]
    public async Task<bool> TryInitialize()
    {
        if (GetType() == typeof(GenshinProcessManager))
        {
            _commandDefinition =
                (await _commandService.GetCommandDefinitionsAsync()).FirstOrDefault(c => c.IsGameStartCommand);
        }
        else if (GetType() == typeof(ThreeDMigtoProcessManager))
        {
            _commandDefinition =
                (await _commandService.GetCommandDefinitionsAsync()).FirstOrDefault(c => c.IsModelImporterCommand);
        }
        else
        {
            throw new InvalidOperationException("Unknown process manager type");
        }

        if (_commandDefinition is null)
        {
            ProcessStatus = ProcessStatus.NotInitialized;

            if (_process is not null)
                Debugger.Break();

            _process = null;
            return false;
        }

        ProcessPath = _commandDefinition.ExecutionOptions.Command;
        ProcessName = _commandDefinition.CommandDisplayName;
        await CheckStatus();
        return true;
    }

    public async Task ResetProcessOptions()
    {
        if (ProcessStatus == ProcessStatus.NotInitialized || _commandDefinition is null) return;

        await _commandService.DeleteCommandDefinitionAsync(_commandDefinition.Id);
        ProcessStatus = ProcessStatus.NotInitialized;
        _commandDefinition = null;
    }


    public async Task StartProcess()
    {
        if (!await TryInitialize())
            return;

        if (ProcessStatus == ProcessStatus.Running && _process is not null)
        {
            _process.Exited -= OnProcessOnExited;
            _process = null;
        }

        if (ProcessStatus == ProcessStatus.NotInitialized)
        {
            _logger.Warning($"{ProcessName} is not initialized");
            return;
        }


        var result = await _commandHandler.RunCommandAsync(_commandDefinition.Id, null);

        if (!result.IsSuccess)
        {
            var errorTitle = _localizer.GetLocalizedStringOrDefault("/Settings/Process_UnableToStartTitle", "Unable to start process");

            if (result.Exception is Win32Exception e)
            {
                var message = string.Format(
                    _localizer.GetLocalizedStringOrDefault("/Settings/Process_StartFailed", "Failed to start {0}"),
                    ProcessName);

                if (e.NativeErrorCode == 1223)
                {
                    message = string.Format(
                        _localizer.GetLocalizedStringOrDefault("/Settings/Process_StartFailed_UAC", "Failed to start {0}. This might be because the User Account Control (UAC) prompt was cancelled."),
                        ProcessName);
                }
                else if (e.NativeErrorCode == 740)
                {
                    message = string.Format(
                        _localizer.GetLocalizedStringOrDefault("/Settings/Process_StartFailed_AdminProp", "Failed to start {0}. This can happen if the 'Run as Administrator' option is enabled in the executable's properties."),
                        ProcessName);
                }

                _notificationManager.ShowNotification(errorTitle, message, null);
                return;
            }


            if (result.HasNotification)
            {
                _notificationManager.ShowNotification(result.Notification);
                return;
            }

            var unknownError = _localizer.GetLocalizedStringOrDefault("/Settings/Process_UnknownError", "An unknown error occurred");
            _notificationManager.ShowNotification(errorTitle, unknownError, null);
            return;
        }

        var process = result.Value;

        if (process.HasExited)
        {
            ProcessStatus = ProcessStatus.NotRunning;
            return;
        }


        process.Exited += OnProcessOnExited;

        _process = process;
    }


    private void OnProcessOnExited(object? sender, EventArgs args)
    {
        ProcessStatus = ProcessStatus.NotRunning;
        _logger.Debug("{ProcessName} exited with exit code: {ExitCode}", ProcessName,
            _process?.ExitCode ?? 13337);
        _process = null;
    }

    public async Task CheckStatus()
    {
        if (_commandDefinition is null)
        {
            ProcessStatus = ProcessStatus.NotInitialized;
            return;
        }

        if (_process is { HasExited: false })
        {
            ProcessStatus = ProcessStatus.Running;
            return;
        }

        var runningCommand = await _commandHandler.GetRunningCommandAsync(_commandDefinition.Id);
        ProcessStatus = runningCommand.Any() ? ProcessStatus.Running : ProcessStatus.NotRunning;
    }

    public async Task StopProcess()
    {
        if (_process is { HasExited: false })
        {
            _logger.Information($"Killing {ProcessName}");
            await _process.KillAsync().ConfigureAwait(false);
            _logger.Debug($"{ProcessName} killed");
        }
    }

    public async Task SetCommandAsync(CommandDefinition commandDefinition)
    {
        await ResetProcessOptions();
        await _commandService.SaveCommandDefinitionAsync(commandDefinition);
        await _commandService.SetSpecialCommands(commandDefinition.Id,
            gameStart: GetType() == typeof(GenshinProcessManager),
            modelImporterStart: GetType() == typeof(ThreeDMigtoProcessManager));
        await TryInitialize();
    }
}

public enum ProcessStatus
{
    /// <summary>
    /// Process path not set and process is not running
    /// </summary>
    NotInitialized,

    /// <summary>
    /// Path set but process not running
    /// </summary>
    NotRunning,

    /// <summary>
    /// Path set and process running
    /// </summary>
    Running
}

public class GenshinProcessManager : BaseProcessManager<GenshinProcessOptions>
{
    public GenshinProcessManager(ILogger logger) : base(logger.ForContext<GenshinProcessManager>())
    {
    }
}

public class ThreeDMigtoProcessManager : BaseProcessManager<MigotoProcessOptions>
{
    public ThreeDMigtoProcessManager(ILogger logger) : base(
        logger.ForContext<ThreeDMigtoProcessManager>())
    {
    }
}

public interface IProcessManager
{
    public bool IsGameProcessManager { get; }
    public string ProcessName { get; }
    public string ProcessPath { get; }
    public ProcessStatus ProcessStatus { get; }

    public Task<bool> TryInitialize();
    public Task ResetProcessOptions();
    public Task StartProcess();
    public Task CheckStatus();
    public Task SetCommandAsync(CommandDefinition commandDefinition);
}