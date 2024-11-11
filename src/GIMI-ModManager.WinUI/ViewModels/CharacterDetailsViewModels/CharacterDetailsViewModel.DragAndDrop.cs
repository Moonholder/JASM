using Windows.Storage;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services.GameBanana;

namespace GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels;

public partial class CharacterDetailsViewModel
{
    public bool CanDragDropMod(IReadOnlyList<IStorageItem>? items)
    {
        if (IsHardBusy)
            return false;

        if (items is null || items.Count != 1)
            return false;

        var mod = items.First();

        var isDirectory = Directory.Exists(mod.Path);

        if (isDirectory)
            return true;

        if (!File.Exists(mod.Path))
            return false;

        var fileExt = Path.GetExtension(mod.Name);

        if (fileExt.IsNullOrEmpty() || !Constants.SupportedArchiveTypes.Contains(fileExt))
            return false;


        return true;
    }

    public async Task DragDropModAsync(IReadOnlyList<IStorageItem> items)
    {
        if (!CanDragDropMod(items))
        {
            _notificationService.ShowNotification("拖放操作失败",
                "操作失败，因为所选项目不是有效的模组文件或文件夹。",
                TimeSpan.FromSeconds(5));
            return;
        }
        await CommandWrapperAsync(true, async () =>
        {
            try
            {
                var installMonitor = await _modDragAndDropService.AddStorageItemFoldersAsync(_modList, items).ConfigureAwait(false);

                if (installMonitor is not null)
                    _ = installMonitor.Task.ContinueWith((task) => ModGridVM.QueueModRefresh(), CancellationToken);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error while adding storage items.");
                _notificationService.ShowNotification("拖放操作失败",
                    $"在添加存储项时发生错误。原因:\n{e.Message}",
                    TimeSpan.FromSeconds(5));
            }
        }).ConfigureAwait(false);
    }

    public bool CanDragDropModUrl(Uri? uri)
    {
        if (IsHardBusy)
            return false;

        if (uri is null)
            return false;

        if (!uri.IsAbsoluteUri)
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (!GameBananaUrlHelper.TryGetModIdFromUrl(uri, out _))
            return false;

        return true;
    }

    public async Task DragDropModUrlAsync(Uri uri)
    {
        if (!CanDragDropModUrl(uri))
        {
            _notificationService.ShowNotification("拖放操作失败",
                "操作失败，因为所选项目不是有效的GameBanana模组链接.",
                TimeSpan.FromSeconds(5));
            return;
        }
        await CommandWrapperAsync(true, async () =>
        {
            try
            {
                await _modDragAndDropService.AddModFromUrlAsync(_modList, uri);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error opening mod page window");
                _notificationService.ShowNotification("打开模组页面窗口时出错", e.Message, TimeSpan.FromSeconds(10));
            }
        }).ConfigureAwait(false);
    }
}