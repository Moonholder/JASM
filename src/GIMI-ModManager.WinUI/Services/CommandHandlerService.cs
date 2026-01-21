using System.ComponentModel;
using Windows.Win32;
using Windows.Win32.Foundation;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services.CommandService;
using GIMI_ModManager.Core.Services.CommandService.Models;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using Serilog;
using GIMI_ModManager.Core.Contracts.Services;

namespace GIMI_ModManager.WinUI.Services;

public class CommandHandlerService(CommandService commandService, ILogger logger, ILanguageLocalizer localizer)
{
    private readonly ILogger _logger = logger.ForContext<CommandHandlerService>();
    private readonly CommandService _commandService = commandService;
    private readonly ILanguageLocalizer _localizer = localizer;


    public async Task<ICollection<string>> CanRunCommandAsync(Guid commandId, SpecialVariablesInput? variablesInput,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await InternalCanRunCommandAsync(commandId, variablesInput, cancellationToken);
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif

            _logger.Error(e, "An error occured when checking if command can be started");
            return [
                string.Format(_localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_ErrorCheckingStart", "An error occurred when checking if command can be started: {0}"), e.Message)
            ];
        }
    }

    private async Task<ICollection<string>> InternalCanRunCommandAsync(Guid commandId,
        SpecialVariablesInput? variablesInput,
        CancellationToken cancellationToken = default)
    {
        var command = await _commandService.GetCommandDefinitionAsync(commandId, cancellationToken: cancellationToken);

        if (command is null)
        {
            return [string.Format(_localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_CommandNotFoundId", "Command with ID '{0}' not found"), commandId)];
        }


        var errors = new List<string>();

        if (command.ExecutionOptions.Command.IsNullOrEmpty())
        {
            errors.Add(_localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_ExecutablePathEmpty", "Executable path value is null or empty"));
        }


        if (!File.Exists(command.ExecutionOptions.Command) && !IsExeFoundInPath(command))
        {
            errors.Add(string.Format(
                _localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_ExecutableNotFound", "Executable '{0}' not found in $PATH or file does not exist"),
                command.ExecutionOptions.Command));
        }


        if (command.ExecutionOptions.WorkingDirectory != SpecialVariables.TargetPath &&
            command.ExecutionOptions.WorkingDirectory is not null &&
            !Directory.Exists(command.ExecutionOptions.WorkingDirectory))
        {
            errors.Add(string.Format(
                _localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_WorkingDirNotFound", "Working directory '{0}' does not exist"),
                command.ExecutionOptions.WorkingDirectory));
        }


        // This check is a bit messy but TargetPath is the only special variable that exists for now
        if (command.ExecutionOptions.HasAnySpecialVariables())
        {
            if (variablesInput is null)
            {
                errors.Add(_localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_SpecialVarsRequired", "Special variables are required for this command"));
            }
            else
            {
                var strings = new[]
                {
                    command.ExecutionOptions.Command, command.ExecutionOptions.WorkingDirectory,
                    command.ExecutionOptions.Arguments
                };

                if (strings.Any(x => x is not null && x.Contains(SpecialVariables.TargetPath)) &&
                    !variablesInput.HasSpecialVariable(SpecialVariables.TargetPath))
                {
                    errors.Add(string.Format(
                        _localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_SpecificVarRequired", "Special variable '{0}' is required for this command"),
                        SpecialVariables.TargetPath));
                }
            }
        }

        var usesTargetPath = command.ExecutionOptions.HasAnySpecialVariables([SpecialVariables.TargetPath]);

        if (usesTargetPath &&
            (variablesInput is null || !variablesInput.HasSpecialVariable(SpecialVariables.TargetPath)))
        {
            errors.Add(string.Format(
                _localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_SpecificVarRequired", "Special variable '{0}' is required for this command"),
                SpecialVariables.TargetPath));
        }

        if (usesTargetPath)
        {
            var targetPath = variablesInput?.GetVariable(SpecialVariables.TargetPath);

            if (targetPath is not null && !Directory.Exists(targetPath))
            {
                errors.Add(string.Format(
                    _localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_TargetPathNotFound", "Target path '{0}' does not exist"),
                    targetPath));
            }
        }

        return errors;
    }

    public async Task<Result<ProcessCommand>> RunCommandAsync(Guid commandId, SpecialVariablesInput? variablesInput,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await InternalRunCommandAsync(commandId, variablesInput, cancellationToken);
        }
        catch (Exception e)
        {
#if DEBUG
            if (e is not Win32Exception)
                throw;
#endif

            _logger.Error(e, "An error occured when starting command");
            var title = _localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_ErrorStartingCommand", "An error occurred when starting command");
            return Result<ProcessCommand>.Error(e, new SimpleNotification(title, e.Message, null));
        }
    }


    private async Task<Result<ProcessCommand>> InternalRunCommandAsync(Guid commandId,
        SpecialVariablesInput? variablesInput,
        CancellationToken cancellationToken)
    {
        var command = await _commandService.GetCommandDefinitionAsync(commandId, cancellationToken: cancellationToken);

        if (command is null)
        {
            var title = _localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_CommandNotFoundTitle", "Command not found");
            var msg = string.Format(_localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_CommandNotFoundId", "Command with ID '{0}' not found"), commandId);
            return Result<ProcessCommand>.Error(new SimpleNotification(title, msg));
        }

        var errors = await InternalCanRunCommandAsync(commandId, variablesInput, cancellationToken);

        if (errors.Count > 0)
        {
            var title = _localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_CommandStartFailedTitle", "Command cannot start");
            var msg = string.Format(
                _localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_CommandStartFailedMessage", "Command '{0}' failed to start due to: {1}"),
                command.CommandDisplayName, string.Join(", ", errors));

            return Result<ProcessCommand>.Error(new SimpleNotification(title, msg));
        }

        var processCommand = _commandService.CreateCommand(command, variablesInput);

        processCommand.Start();


        var successTitle = _localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_CommandStartedTitle", "Command started");
        var successMsg = string.Format(
            _localizer.GetLocalizedStringOrDefault("/Settings/CommandHandler_CommandStartedMessage", "Command '{0}' started"),
            command.CommandDisplayName);

        return Result<ProcessCommand>.Success(processCommand, new SimpleNotification(successTitle, successMsg));
    }

    public async Task<List<CommandDefinition>> GetCommandsThatContainSpecialVariablesAsync(
        params string[] specialVariable)
    {
        if (!specialVariable.Where(s => !s.IsNullOrEmpty()).Any(s => SpecialVariables.AllVariables.Contains(s)))
            return [];


        var commands = await _commandService.GetCommandDefinitionsAsync();

        return commands.Where(x => x.ExecutionOptions.HasAnySpecialVariables(specialVariable)).ToList();
    }

    private unsafe bool IsExeFoundInPath(CommandDefinition commandDefinition)
    {
        var index = 0;
        var charBuffer = new Span<char>(new char[500]);

        var command = commandDefinition.ExecutionOptions.Command;

        command = command.EndsWith(".exe") ? command : command + ".exe";

        foreach (var c in command.AsEnumerable().Append('\0'))
        {
            charBuffer[index] = c;
            index++;
        }

        if (!charBuffer.IsEmpty && charBuffer.LastIndexOf('\0') == -1)
            throw new ArgumentException("Required null terminator missing.");

        fixed (char* p = charBuffer)
        {
            var result = PInvoke.PathFindOnPath(new PWSTR(p));

            return result;
        }
    }

    public async Task<IEnumerable<ProcessCommand>> GetRunningCommandAsync(Guid commandDefinitionId)
    {
        return (await _commandService.GetRunningCommandsAsync().ConfigureAwait(false))
            .Where(x => x.CommandDefinitionId == commandDefinitionId)
            .ToArray();
    }
}