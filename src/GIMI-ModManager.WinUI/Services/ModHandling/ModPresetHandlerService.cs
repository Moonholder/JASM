using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.Core.Services.ModPresetService;
using GIMI_ModManager.Core.Services.ModPresetService.Models;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Services.Notifications;
using Serilog;

namespace GIMI_ModManager.WinUI.Services.ModHandling;

public sealed class ModPresetHandlerService(
    ILogger logger,
    ModPresetService modPresetService,
    UserPreferencesService preferencesService,
    NotificationManager notificationManager,
    ElevatorService elevatorService,
    ILocalSettingsService localSettingsService)
{
    private readonly ILogger _logger = logger.ForContext<ModPresetHandlerService>();
    private readonly ModPresetService _modPresetService = modPresetService;
    private readonly UserPreferencesService _userPreferencesService = preferencesService;
    private readonly NotificationManager _notificationManager = notificationManager;
    private readonly ElevatorService _elevatorService = elevatorService;
    private readonly ILocalSettingsService _localSettingsService = localSettingsService;


    public Task<IEnumerable<ModPreset>> GetModPresetsAsync()
        => Task.FromResult(_modPresetService.GetPresets().OrderBy(p => p.Index).AsEnumerable());

    public async Task<Result> ApplyModPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        try
        {
            return await InternalModPresetAsync(presetName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif

            _logger.Error(e, "An error occured when applying preset {PresetName}", presetName);
            return Result.Error(new SimpleNotification("应用模组预设失败", e.Message, null));
        }
    }


    private async Task<Result> InternalModPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetName, nameof(presetName));
        await _modPresetService.ApplyPresetAsync(presetName, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var preferencesResult = await _userPreferencesService
            .SetModPreferencesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!preferencesResult)
        {
            _notificationManager.ShowNotification("无法写入模组设置到 3Dmigoto user .ini",
                "详细信息请参见日志", null);
        }


        var modPreset = _modPresetService.GetPreset(presetName);


        var simpleNotification = new SimpleNotification
        (
            "应用模组预设",
            $"模组预设 {modPreset.Name} 已应用",
            TimeSpan.FromSeconds(5)
        );


        if (await CanAutoSyncAsync().ConfigureAwait(false))
        {
            await _elevatorService.RefreshGenshinMods().ConfigureAwait(false);
            if (modPreset.Mods.Count == 0)
                return Result.Success(simpleNotification);

            await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
            await _userPreferencesService.SetModPreferencesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }


        if (await CanAutoSyncAsync().ConfigureAwait(false))
        {
            //await ElevatorService.RefreshGenshinMods().ConfigureAwait(false); // Wait and check for changes timout 5 seconds
            //await Task.Delay(5000).ConfigureAwait(false);
            await _elevatorService.RefreshAndWaitForUserIniChangesAsync().ConfigureAwait(false);
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            await _userPreferencesService.SetModPreferencesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }


        if (await CanAutoSyncAsync().ConfigureAwait(false))
        {
            await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
            await _elevatorService.RefreshGenshinMods().ConfigureAwait(false);
        }


        return Result.Success(simpleNotification);
    }


    private async Task<bool> CanAutoSyncAsync()
    {
        var autoSync = await _localSettingsService.ReadOrCreateSettingAsync<ModPresetSettings>(ModPresetSettings.Key)
            .ConfigureAwait(false);

        return _elevatorService.CheckStatus() == ElevatorStatus.Running && autoSync.AutoSyncMods;
    }

    public async Task<Result> SaveActiveModPreferencesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await InternalSaveActivePreferencesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif

            _logger.Error(e, "An error occured when saving active preferences");
            return Result.Error(new SimpleNotification("保存激活的偏好设置失败", e.Message, null));
        }
    }

    private async Task<Result> InternalSaveActivePreferencesAsync(CancellationToken cancellationToken)
    {
        await _userPreferencesService.SaveModPreferencesAsync().ConfigureAwait(false);

        return Result.Success(new SimpleNotification("激活的偏好设置已保存",
            $"存储在 {Constants.UserIniFileName} 的偏好设置已为启用的模组保存",
            TimeSpan.FromSeconds(5)));
    }

    public async Task<Result> ApplyActiveModPreferencesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await InternalApplyActivePreferencesAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif

            _logger.Error(e, "An error occured when applying active preferences");
            return Result.Error(new SimpleNotification("应用保存的偏好设置失败", e.Message, null));
        }
    }

    private async Task<Result> InternalApplyActivePreferencesAsync(CancellationToken cancellationToken)
    {
        await _userPreferencesService.SetModPreferencesAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new SimpleNotification("已应用保存的偏好设置",
            $"模组偏好设置已写入 3DMigoto {Constants.UserIniFileName}",
            TimeSpan.FromSeconds(5)));
    }
}