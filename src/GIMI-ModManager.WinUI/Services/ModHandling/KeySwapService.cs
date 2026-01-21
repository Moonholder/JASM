using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.WinUI.Services.Notifications;
using Serilog;


namespace GIMI_ModManager.WinUI.Services.ModHandling;

public interface IKeySwapService
{
    Task<Result<Dictionary<string, List<KeySwapSection>>>> GetAllKeySwapsAsync(Guid modId, bool showDisabledIniFiles);
    Task<Result> SaveKeySwapsAsync(
        Guid modId, Dictionary<string, List<KeySwapSection>> keySwapsByFile);
}

public class KeySwapService : IKeySwapService
{
    private readonly ISkinManagerService _skinManagerService;
    private readonly ILogger _logger;
    private readonly NotificationManager _notificationManager;
    private readonly ILanguageLocalizer _localizer;

    public KeySwapService(ISkinManagerService skinManagerService,
                          ILogger logger,
                          NotificationManager notificationManager,
                          ILanguageLocalizer localizer)
    {
        _skinManagerService = skinManagerService;
        _logger = logger.ForContext<KeySwapService>();
        _notificationManager = notificationManager;
        _localizer = localizer;
    }

    public async Task<Result<Dictionary<string, List<KeySwapSection>>>> GetAllKeySwapsAsync(Guid modId, bool showDisabledIniFiles)
    {
        try
        {
            var mod = _skinManagerService.GetModById(modId);
            if (mod is null)
                return Result<Dictionary<string, List<KeySwapSection>>>.Error(new SimpleNotification(
                    _localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_ModNotFoundTitle", "Mod not found"),
                    string.Format(_localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_ModNotFoundMessage", "Could not find mod with ID {0}"), modId),
                    TimeSpan.FromSeconds(5)));

            if (mod.KeySwaps is null)
                return Result<Dictionary<string, List<KeySwapSection>>>.Success(new Dictionary<string, List<KeySwapSection>>(),
                    new SimpleNotification(
                        _localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_NotSupportedTitle", "Key swaps not supported"),
                        _localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_NotSupportedMessage", "This mod does not support key swapping"),
                        TimeSpan.FromSeconds(3)));

            var keySwaps = await mod.KeySwaps.ReadAllKeySwapConfigurations(showDisabledIniFiles).ConfigureAwait(false);
            return Result<Dictionary<string, List<KeySwapSection>>>.Success(keySwaps);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load mod key swap configuration: {ModId}", modId);
            return Result<Dictionary<string, List<KeySwapSection>>>.Error(ex, new SimpleNotification(
                _localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_LoadFailedTitle", "Failed to load key swaps"),
                string.Format(_localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_LoadFailedMessage", "Could not load mod key configuration: {0}"), ex.Message),
                TimeSpan.FromSeconds(5)));
        }
    }

    public async Task<Result> SaveKeySwapsAsync(Guid modId, Dictionary<string, List<KeySwapSection>> keySwapsByFile)
    {
        try
        {
            var mod = _skinManagerService.GetModById(modId);
            if (mod is null)
                return Result.Error(new SimpleNotification(
                    _localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_ModNotFoundTitle", "Mod not found"),
                    string.Format(_localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_ModNotFoundMessage", "Could not find mod with ID {0}"), modId),
                    TimeSpan.FromSeconds(5)));

            if (mod.KeySwaps is null)
                return Result.Error(new SimpleNotification(
                    _localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_NotSupportedTitle", "Key swaps not supported"),
                    _localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_NotSupportedMessage", "This mod does not support key swapping"),
                    TimeSpan.FromSeconds(3)));



            await mod.KeySwaps.SaveAllKeySwapConfigurations(keySwapsByFile).ConfigureAwait(false);

            _logger.Information("Saved mod key configuration successfully: {ModId}, {FileCount} files, {SectionCount} sections",
                modId, keySwapsByFile.Count, keySwapsByFile.Sum(f => f.Value.Count));

            return Result.Success(new SimpleNotification(
                _localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_SaveSuccessTitle", "Saved successfully"),
                _localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_SaveSuccessMessage", "Key configuration updated"),
                TimeSpan.FromSeconds(3)));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save mod key configuration: {ModId}", modId);
            return Result.Error(ex, new SimpleNotification(
                _localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_SaveFailedTitle", "Failed to save key swaps"),
                string.Format(_localizer.GetLocalizedStringOrDefault("/Settings/KeySwap_SaveFailedMessage", "Error saving mod key configuration: {0}"), ex.Message),
                TimeSpan.FromSeconds(5)));
        }
    }
}