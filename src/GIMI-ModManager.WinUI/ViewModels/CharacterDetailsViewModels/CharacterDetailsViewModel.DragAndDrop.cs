using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.Core.Services.GameBanana;
using Windows.Storage;

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

    public async Task DragDropModAsync(IReadOnlyList<IStorageItem> items, ICharacterSkin? inGameSkin = null)
    {
        if (!CanDragDropMod(items))
        {
            var localizer = App.GetService<ILanguageLocalizer>();
            _notificationService.ShowNotification(
                localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DragDropFailedTitle", "Drag and drop failed"),
                localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/InvalidStorageItemError", "Operation failed because the selected item is not a valid mod file or folder."),
                TimeSpan.FromSeconds(5));
            return;
        }
        await CommandWrapperAsync(true, async () =>
        {
            try
            {
                var installMonitor = await Task.Run(async () =>
                    await _modDragAndDropService.AddStorageItemFoldersAsync(_modList, items, inGameSkin).ConfigureAwait(false), CancellationToken);

                if (installMonitor is not null)
                    _ = installMonitor.Task.ContinueWith((task) => ModGridVM.QueueModRefresh(), CancellationToken);
            }
            catch (Exception e)
            {
                var localizer = App.GetService<ILanguageLocalizer>();
                _logger.Error(e, "Error while adding storage items.");
                _notificationService.ShowNotification(
                    localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DragDropFailedTitle", "Drag and drop failed"),
                    string.Format(localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/ErrorAddingStorageItemFormat", "An error occurred while adding storage items. Reason:\n{0}"), e.Message),
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

        if (!GameBananaUrlHelper.TryGetModIdFromUrl(uri, out _, out var modelName))
            return false;

        // Request and Question types don't have downloadable files
        if (!GameBananaUrlHelper.HasDownloadableFiles(modelName))
            return false;

        return true;
    }

    public async Task DragDropModUrlAsync(Uri uri)
    {
        if (!CanDragDropModUrl(uri))
        {
            var localizer = App.GetService<ILanguageLocalizer>();
            _notificationService.ShowNotification(
                localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/DragDropFailedTitle", "Drag and drop failed"),
                localizer.GetLocalizedStringOrDefault("/CharacterDetailsPage/InvalidModUrlError", "Operation failed because the selected item is not a valid GameBanana mod link."),
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