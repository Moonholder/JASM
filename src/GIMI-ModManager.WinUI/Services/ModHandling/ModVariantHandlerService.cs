using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.Core.Services.ModPresetService;
using GIMI_ModManager.Core.Services.ModPresetService.Models;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Services.Notifications;
using Serilog;

namespace GIMI_ModManager.WinUI.Services.ModHandling;

public sealed class ModVariantHandlerService(
    ILogger logger,
    ModVariantService modVariantService,
    UserPreferencesService preferencesService,
    NotificationManager notificationManager,
    ElevatorService elevatorService,
    ILocalSettingsService localSettingsService,
    ILanguageLocalizer localizer)
{
    private readonly ILogger _logger = logger.ForContext<ModVariantHandlerService>();
    private readonly ModVariantService _modVariantService = modVariantService;
    private readonly UserPreferencesService _userPreferencesService = preferencesService;
    private readonly NotificationManager _notificationManager = notificationManager;
    private readonly ElevatorService _elevatorService = elevatorService;
    private readonly ILocalSettingsService _localSettingsService = localSettingsService;
    private readonly ILanguageLocalizer _localizer = localizer;

    public async Task<Result> SaveCurrentAsVariantAsync(Guid modId, string name, CancellationToken ct)
    {
        try
        {
            if (_elevatorService.CheckStatus() == ElevatorStatus.Running)
            {
                await _elevatorService.RefreshAndWaitForUserIniChangesAsync().ConfigureAwait(false);
            }

            await _modVariantService.SaveCurrentAsVariantAsync(modId, name, ct).ConfigureAwait(false);
            var title = _localizer.GetLocalizedStringOrDefault("/ModVariant/SaveSuccessTitle", "Save Variant");
            var message = string.Format(_localizer.GetLocalizedStringOrDefault("/ModVariant/SaveSuccessMessage", "Variant '{0}' saved successfully"), name);
            return Result.Success(new SimpleNotification(title, message, TimeSpan.FromSeconds(5)));
        }
        catch (Exception e)
        {
            _logger.Error(e, "An error occurred when saving variant {VariantName} for mod {ModId}", name, modId);
            var title = _localizer.GetLocalizedStringOrDefault("/ModVariant/SaveFailedTitle", "Failed to save variant");
            return Result.Error(new SimpleNotification(title, GetLocalizedErrorMessage(e), TimeSpan.FromSeconds(5)));
        }
    }

    public async Task<Result> ApplyVariantAsync(Guid modId, string name, CancellationToken ct)
    {
        try
        {
            await _modVariantService.ApplyVariantAsync(modId, name, ct).ConfigureAwait(false);

            var title = _localizer.GetLocalizedStringOrDefault("/ModVariant/ApplySuccessTitle", "Apply Variant");
            var message = string.Format(_localizer.GetLocalizedStringOrDefault("/ModVariant/ApplySuccessMessage", "Variant '{0}' applied successfully"), name);
            var simpleNotification = new SimpleNotification(title, message, TimeSpan.FromSeconds(5));

            if (await CanAutoSyncAsync().ConfigureAwait(false))
            {
                try
                {
                    _userPreferencesService.SetUserIniReadOnly(true);
                    await _elevatorService.RefreshGenshinMods().ConfigureAwait(false);
                }
                finally
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000).ConfigureAwait(false);
                        _userPreferencesService.SetUserIniReadOnly(false);
                    });
                }
            }

            return Result.Success(simpleNotification);
        }
        catch (Exception e)
        {
            _logger.Error(e, "An error occurred when applying variant {VariantName} for mod {ModId}", name, modId);
            var title = _localizer.GetLocalizedStringOrDefault("/ModVariant/ApplyFailedTitle", "Failed to apply variant");
            return Result.Error(new SimpleNotification(title, GetLocalizedErrorMessage(e), TimeSpan.FromSeconds(5)));
        }
    }

    public async Task<Result> DeleteVariantAsync(Guid modId, string name, CancellationToken ct)
    {
        try
        {
            await _modVariantService.DeleteVariantAsync(modId, name, ct).ConfigureAwait(false);
            var title = _localizer.GetLocalizedStringOrDefault("/ModVariant/DeleteSuccessTitle", "Delete Variant");
            var message = string.Format(_localizer.GetLocalizedStringOrDefault("/ModVariant/DeleteSuccessMessage", "Variant '{0}' deleted successfully"), name);

            if (await CanAutoSyncAsync().ConfigureAwait(false))
            {
                await _elevatorService.RefreshGenshinMods().ConfigureAwait(false);
            }

            return Result.Success(new SimpleNotification(title, message, TimeSpan.FromSeconds(5)));
        }
        catch (Exception e)
        {
            _logger.Error(e, "An error occurred when deleting variant {VariantName} for mod {ModId}", name, modId);
            var title = _localizer.GetLocalizedStringOrDefault("/ModVariant/DeleteFailedTitle", "Failed to delete variant");
            return Result.Error(new SimpleNotification(title, GetLocalizedErrorMessage(e), TimeSpan.FromSeconds(5)));
        }
    }

    public async Task<Result> RenameVariantAsync(Guid modId, string oldName, string newName, CancellationToken ct)
    {
        try
        {
            await _modVariantService.RenameVariantAsync(modId, oldName, newName, ct).ConfigureAwait(false);
            var title = _localizer.GetLocalizedStringOrDefault("/ModVariant/RenameSuccessTitle", "Rename Variant");
            var message = string.Format(_localizer.GetLocalizedStringOrDefault("/ModVariant/RenameSuccessMessage", "Variant renamed to '{0}' successfully"), newName);
            return Result.Success(new SimpleNotification(title, message, TimeSpan.FromSeconds(5)));
        }
        catch (Exception e)
        {
            _logger.Error(e, "An error occurred when renaming variant '{OldName}' to '{NewName}' for mod {ModId}", oldName, newName, modId);
            var title = _localizer.GetLocalizedStringOrDefault("/ModVariant/RenameFailedTitle", "Failed to rename variant");
            return Result.Error(new SimpleNotification(title, GetLocalizedErrorMessage(e), TimeSpan.FromSeconds(5)));
        }
    }

    public Task<IReadOnlyList<ModVariantPreset>> GetVariantsAsync(Guid modId)
        => _modVariantService.GetVariantsAsync(modId);

    public Task<string?> GetActiveVariantNameAsync(Guid modId)
        => _modVariantService.GetActiveVariantNameAsync(modId);

    private async Task<bool> CanAutoSyncAsync()
    {
        var autoSync = await _localSettingsService.ReadOrCreateSettingAsync<ModPresetSettings>(ModPresetSettings.Key)
            .ConfigureAwait(false);

        return _elevatorService.CheckStatus() == ElevatorStatus.Running && autoSync.AutoSyncMods;
    }

    private string GetLocalizedErrorMessage(Exception e)
    {
        if (e is ArgumentException argEx)
        {
            if (argEx.Message.Contains("Variant name cannot be empty") || argEx.Message.Contains("New variant name cannot be empty"))
            {
                return _localizer.GetLocalizedStringOrDefault("/ModVariant/Error_NameEmpty", "Variant name cannot be empty");
            }
            if (argEx.Message.Contains("was not found"))
            {
                var start = argEx.Message.IndexOf('\'');
                var end = argEx.Message.LastIndexOf('\'');
                if (start >= 0 && end > start)
                {
                    var name = argEx.Message.Substring(start + 1, end - start - 1);
                    return string.Format(_localizer.GetLocalizedStringOrDefault("/ModVariant/Error_VariantNotFound", "Variant named '{0}' was not found"), name);
                }
                return _localizer.GetLocalizedStringOrDefault("/ModVariant/Error_VariantNotFound", "Variant was not found");
            }
        }
        else if (e is InvalidOperationException invEx)
        {
            if (invEx.Message.Contains("Failed to read mod settings"))
            {
                return _localizer.GetLocalizedStringOrDefault("/ModVariant/Error_ReadSettingsFailed", "Failed to read mod settings");
            }
            if (invEx.Message.Contains("Failed to read variant configuration file"))
            {
                return _localizer.GetLocalizedStringOrDefault("/ModVariant/Error_ReadConfigFailed", "Failed to read variant configuration file");
            }
            if (invEx.Message.Contains("Failed to apply variant to d3dx_user.ini"))
            {
                return _localizer.GetLocalizedStringOrDefault("/ModVariant/Error_ApplyIniFailed", "Failed to apply variant to d3dx_user.ini");
            }
            if (invEx.Message.Contains("already exists"))
            {
                var start = invEx.Message.IndexOf('\'');
                var end = invEx.Message.LastIndexOf('\'');
                if (start >= 0 && end > start)
                {
                    var name = invEx.Message.Substring(start + 1, end - start - 1);
                    return string.Format(_localizer.GetLocalizedStringOrDefault("/ModVariant/Error_VariantAlreadyExists", "A variant named '{0}' already exists"), name);
                }
                return _localizer.GetLocalizedStringOrDefault("/ModVariant/Error_VariantAlreadyExists", "A variant with this name already exists");
            }
        }
        else if (e is FileNotFoundException fileEx)
        {
            if (fileEx.Message.Contains("Variant configuration file not found"))
            {
                return _localizer.GetLocalizedStringOrDefault("/ModVariant/Error_ConfigNotFound", "Variant configuration file not found");
            }
        }

        return e.Message;
    }
}