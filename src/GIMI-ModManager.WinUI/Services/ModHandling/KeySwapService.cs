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

    public KeySwapService(ISkinManagerService skinManagerService,
                          ILogger logger,
                          NotificationManager notificationManager)
    {
        _skinManagerService = skinManagerService;
        _logger = logger.ForContext<KeySwapService>();
        _notificationManager = notificationManager;
    }

    public async Task<Result<Dictionary<string, List<KeySwapSection>>>> GetAllKeySwapsAsync(Guid modId, bool showDisabledIniFiles)
    {
        try
        {
            var mod = _skinManagerService.GetModById(modId);
            if (mod is null)
                return Result<Dictionary<string, List<KeySwapSection>>>.Error(new SimpleNotification("模组未找到", $"找不到ID为 {modId} 的模组", TimeSpan.FromSeconds(5)));

            if (mod.KeySwaps is null)
                return Result<Dictionary<string, List<KeySwapSection>>>.Success(new Dictionary<string, List<KeySwapSection>>(),
                    new SimpleNotification("键位交换不支持", "当前模组不支持键位交换功能", TimeSpan.FromSeconds(3)));

            var keySwaps = await mod.KeySwaps.ReadAllKeySwapConfigurations(showDisabledIniFiles).ConfigureAwait(false);
            return Result<Dictionary<string, List<KeySwapSection>>>.Success(keySwaps);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "加载模组键位配置失败: {ModId}", modId);
            return Result<Dictionary<string, List<KeySwapSection>>>.Error(ex, new SimpleNotification("键位交换加载失败",
                $"无法加载模组的键位配置: {ex.Message}", TimeSpan.FromSeconds(5)));
        }
    }

    public async Task<Result> SaveKeySwapsAsync(Guid modId, Dictionary<string, List<KeySwapSection>> keySwapsByFile)
    {
        try
        {
            var mod = _skinManagerService.GetModById(modId);
            if (mod is null)
                return Result.Error(new SimpleNotification("模组未找到", $"找不到ID为 {modId} 的模组", TimeSpan.FromSeconds(5)));

            if (mod.KeySwaps is null)
                return Result.Error(new SimpleNotification("键位交换不支持", "当前模组不支持键位交换功能", TimeSpan.FromSeconds(3)));



            await mod.KeySwaps.SaveAllKeySwapConfigurations(keySwapsByFile).ConfigureAwait(false);

            _logger.Information("保存模组键位配置成功: {ModId}, {FileCount} 文件, {SectionCount} 节",
                modId, keySwapsByFile.Count, keySwapsByFile.Sum(f => f.Value.Count));

            return Result.Success(new SimpleNotification("保存成功", "键位配置已更新", TimeSpan.FromSeconds(3)));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "保存模组键位配置失败: {ModId}", modId);
            return Result.Error(ex, new SimpleNotification("键位交换保存失败",
                $"保存模组的键位配置时出错: {ex.Message}", TimeSpan.FromSeconds(5)));
        }
    }
}