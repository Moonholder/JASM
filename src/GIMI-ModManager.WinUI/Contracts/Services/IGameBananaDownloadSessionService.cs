using System.Collections.ObjectModel;
using System.Threading.Tasks;
using GIMI_ModManager.Core.Services.GameBanana.Models;
using GIMI_ModManager.Core.Services.GameBanana.ApiModels;
using GIMI_ModManager.WinUI.ViewModels;

namespace GIMI_ModManager.WinUI.Contracts.Services;

public interface IGameBananaDownloadSessionService
{
    ObservableCollection<GbDownloadTask> DownloadQueue { get; }

    Task LoadDownloadHistoryAsync();

    void DownloadAndInstall(ModFileInfo fileInfo, GbModDisplayItem selectedMod, ModPageInfo selectedModDetail, string? gameId);

    void CancelDownloadTask(GbDownloadTask? task);

    void RemoveDownloadTask(GbDownloadTask? task);

    void ClearAllCompletedTasks();

    void RetryDownloadTask(GbDownloadTask? task);

    void CleanupDownloadAssets(GbDownloadTask task);
}