using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.Core.Services.GameBanana;
using GIMI_ModManager.Core.Services.GameBanana.Models;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog;
using System.Collections.ObjectModel;

namespace GIMI_ModManager.WinUI.Services.ModHandling;

public class GameBananaDownloadSessionService : IGameBananaDownloadSessionService
{
    private readonly GameBananaCoreService _gbService;
    private readonly IGameService _gameService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly ModInstallerService _modInstallerService;
    private readonly ArchiveService _archiveService;
    private readonly NotificationManager _notificationManager;
    private readonly IWindowManagerService _windowManagerService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILanguageLocalizer _localizer;
    private readonly ILogger _logger;
    private readonly DispatcherQueue _dispatcherQueue;

    public ObservableCollection<GbDownloadTask> DownloadQueue { get; } = new();
    private readonly SemaphoreSlim _downloadQueueLock = new(1, 1);
    private readonly SemaphoreSlim _historyLock = new(1, 1);

    public GameBananaDownloadSessionService(
        GameBananaCoreService gbService,
        IGameService gameService,
        ISkinManagerService skinManagerService,
        ModInstallerService modInstallerService,
        ArchiveService archiveService,
        NotificationManager notificationManager,
        IWindowManagerService windowManagerService,
        ILocalSettingsService localSettingsService,
        ILanguageLocalizer localizer,
        ILogger logger)
    {
        _gbService = gbService;
        _gameService = gameService;
        _skinManagerService = skinManagerService;
        _modInstallerService = modInstallerService;
        _archiveService = archiveService;
        _notificationManager = notificationManager;
        _windowManagerService = windowManagerService;
        _localSettingsService = localSettingsService;
        _localizer = localizer;
        _logger = logger.ForContext<GameBananaDownloadSessionService>();

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task LoadDownloadHistoryAsync()
    {
        try
        {
            if (DownloadQueue.Any()) return; // Already loaded

            var history = await _localSettingsService.ReadSettingAsync<List<DownloadHistoryEntry>>(GameBananaSettings.DownloadHistoryKey);
            if (history != null && history.Any())
            {
                var dict = new Dictionary<string, GbDownloadTask>();

                foreach (var entry in history)
                {
                    var task = new GbDownloadTask
                    {
                        Mod = new GbModDisplayItem { Name = entry.ModName, ModelName = string.IsNullOrWhiteSpace(entry.ModelName) ? "Mod" : entry.ModelName },
                        FileInfo = new ModFileInfo(!string.IsNullOrEmpty(entry.ModId) ? entry.ModId : "0", entry.FileId, entry.FileName, string.Empty, entry.Md5Checksum ?? string.Empty, DateTime.MinValue)
                        {
                            DownloadUrl = entry.DownloadUrl
                        },
                        CategoryName = entry.CategoryName,
                        ModUrl = entry.ModUrl,
                        StatusMessage = entry.StatusMessage,
                        ArchivePath = entry.ArchivePath,
                        IsError = entry.IsError,
                        IsCompleted = entry.IsCompleted,
                        ProgressPercentage = entry.ProgressPercentage
                    };
                    dict[entry.FileId] = task;
                }

                _dispatcherQueue.TryEnqueue(() =>
                {
                    DownloadQueue.Clear();
                    foreach (var task in dict.Values.OrderBy(t => t.IsCompleted).ThenBy(t => t.IsError))
                    {
                        DownloadQueue.Add(task);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load download history");
        }
    }

    private async Task SaveDownloadHistoryAsync()
    {
        await _historyLock.WaitAsync();
        try
        {
            var entries = DownloadQueue
                .Where(t => t.IsCompleted || t.IsError)
                .TakeLast(20) // Keep max 20 entries
                .Select(t => new DownloadHistoryEntry
                {
                    ModId = t.FileInfo?.ModId ?? "",
                    CategoryName = t.CategoryName ?? "",
                    ModUrl = t.ModUrl ?? "",
                    ModName = t.Mod?.Name ?? "",
                    ModelName = t.Mod?.ModelName ?? "Mod",
                    DownloadUrl = t.FileInfo?.DownloadUrl ?? "",
                    Md5Checksum = t.FileInfo?.Md5Checksum ?? "",
                    FileName = t.FileInfo?.FileName ?? "",
                    FileId = t.FileInfo?.FileId ?? "",
                    StatusMessage = t.StatusMessage,
                    ArchivePath = t.ArchivePath ?? "",
                    IsError = t.IsError,
                    IsCompleted = t.IsCompleted,
                    ProgressPercentage = t.ProgressPercentage
                })
                .ToList();

            await _localSettingsService.SaveSettingAsync(
                GameBananaSettings.DownloadHistoryKey, entries);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to save download history");
        }
        finally
        {
            _historyLock.Release();
        }
    }

    public void DownloadAndInstall(ModFileInfo fileInfo, GbModDisplayItem selectedMod, ModPageInfo selectedModDetail, string? gameId)
    {
        if (fileInfo == null || selectedModDetail == null || selectedMod == null) return;
        if (string.IsNullOrEmpty(gameId)) return;

        // Prevent duplicate downloads, but remove older completed/error tasks if retrying
        var existingTask = DownloadQueue.FirstOrDefault(t => t.FileInfo?.FileId == fileInfo.FileId);
        if (existingTask != null)
        {
            if (!existingTask.IsCompleted && !existingTask.IsError)
            {
                var title = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/InQueueTitle", "Already In Queue");
                var msgTpl = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/InQueueMessage", "\"{0}\" is already in the download queue");
                _notificationManager.ShowNotification(title, string.Format(msgTpl, fileInfo.FileName), TimeSpan.FromSeconds(3));
                return;
            }
            // Remove the old task so we can add a fresh one without duplicating records
            DownloadQueue.Remove(existingTask);
        }

        var cts = new CancellationTokenSource();
        fileInfo.IsDownloading = true;
        var downloadTask = new GbDownloadTask
        {
            Mod = selectedMod,
            FileInfo = fileInfo,
            CategoryName = selectedModDetail.CategoryName,
            ModUrl = selectedModDetail?.ModPageUrl?.ToString(),
            Cts = cts
        };
        DownloadQueue.Add(downloadTask);

        var addedTitle = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/TaskAddedTitle", "Task Added to Queue");
        var addedMsgTpl = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/TaskAddedMessage", "Downloading {0} in background...");
        _notificationManager.ShowNotification(addedTitle, string.Format(addedMsgTpl, selectedMod?.Name), TimeSpan.FromSeconds(3));

        _ = Task.Run(() => ProcessDownloadTaskAsync(downloadTask));
    }

    public void CancelDownloadTask(GbDownloadTask? task)
    {
        if (task is not { IsCompleted: false }) return;
        task.Cts?.Cancel();
        _dispatcherQueue.TryEnqueue(() =>
        {
            task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusCancelled", "Cancelled");
            task.IsCompleted = true;
            task.IsError = true;
            if (task.FileInfo != null) task.FileInfo.IsDownloading = false;
            _ = SaveDownloadHistoryAsync();
        });
    }

    public void CleanupDownloadAssets(GbDownloadTask task)
    {
        if (!string.IsNullOrEmpty(task.ArchivePath) && System.IO.File.Exists(task.ArchivePath))
        {
            try { File.Delete(task.ArchivePath); } catch { }
        }
    }

    public void RemoveDownloadTask(GbDownloadTask? task)
    {
        if (task == null) return;
        DownloadQueue.Remove(task);
        CleanupDownloadAssets(task);
        _ = SaveDownloadHistoryAsync();
    }

    public void ClearAllCompletedTasks()
    {
        var toRemove = DownloadQueue.Where(t => t.IsCompleted || t.IsError).ToList();
        foreach (var task in toRemove)
        {
            DownloadQueue.Remove(task);
            CleanupDownloadAssets(task);
        }
        _ = SaveDownloadHistoryAsync();
    }

    public void RetryDownloadTask(GbDownloadTask? task)
    {
        if (task == null) return;
        if (!task.IsCompleted && !task.IsError) return;

        task.IsCompleted = false;
        task.IsError = false;
        task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusPreparing", "Preparing...");
        task.ProgressPercentage = 0;
        task.Cts = new CancellationTokenSource();

        _ = Task.Run(() => ProcessDownloadTaskAsync(task));
    }

    private async Task ProcessDownloadTaskAsync(GbDownloadTask task)
    {
        bool lockAcquired = false;
        try
        {
            var progress = new Progress<int>(p =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    task.ProgressPercentage = p;
                    var dlMsgTpl = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusDownloading", "Downloading {0}%");
                    task.StatusMessage = string.Format(dlMsgTpl, p);
                });
            });

            string archivePath;
            if (!string.IsNullOrWhiteSpace(task.FileInfo.DownloadUrl))
            {
                archivePath = await Task.Run(
                    () => _gbService.DownloadModByDirectUrlAsync(task.FileInfo, progress, task.Cts?.Token ?? CancellationToken.None));
            }
            else
            {
                var identifier = new GbModFileIdentifier(
                    new GbModId(task.FileInfo.ModId), new GbModFileId(task.FileInfo.FileId));
                archivePath = await Task.Run(
                    () => _gbService.DownloadModAsync(identifier, task.Mod?.ModelName ?? "Mod", progress, task.Cts?.Token ?? CancellationToken.None));
            }

            task.ArchivePath = archivePath;

            if (!ArchiveService.IsArchive(archivePath))
            {
                _dispatcherQueue.TryEnqueue(async () =>
                {
                    task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusDownloadToCache", "");
                    task.IsCompleted = true;
                    task.ProgressPercentage = 100;
                    if (task.FileInfo != null) task.FileInfo.IsDownloading = false;
                    _ = SaveDownloadHistoryAsync();

                    var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                    {
                        Title = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/RawFileDownloadedTitle", "Raw File Downloaded"),
                        Content = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/RawFileDownloadedMsg", "This item is a standalone file or tool, not a standard mod archive, and has been saved to the staging area."),
                        PrimaryButtonText = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/RawFileOpenDir", "Show in Explorer"),
                        CloseButtonText = _localizer.GetLocalizedStringOrDefault("Common_Close", "Close"),
                        DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style
                    };

                    var res = await _windowManagerService.ShowDialogAsync(dialog);
                    if (res == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{archivePath}\"");
                        }
                        catch { }
                    }
                });
                return;
            }

            _dispatcherQueue.TryEnqueue(() => task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusMatching", "Matching Category..."));

            IModdableObject? targetCharacter = null;
            var matchScores = new Dictionary<IModdableObject, int>();

            void UpdateScores(string? query)
            {
                if (string.IsNullOrWhiteSpace(query)) return;
                var dict = _gameService.QueryModdableObjects(query);
                foreach (var kv in dict)
                {
                    if (!matchScores.TryGetValue(kv.Key, out var existingScore) || kv.Value > existingScore)
                    {
                        matchScores[kv.Key] = kv.Value;
                    }
                }
            }

            UpdateScores(task.CategoryName);

            if (matchScores.Count > 0)
            {
                var bestMatch = matchScores.OrderByDescending(x => x.Value).First();
                if (bestMatch.Value >= 150)
                {
                    targetCharacter = bestMatch.Key;
                }
            }

            if (targetCharacter == null)
            {
                _dispatcherQueue.TryEnqueue(() => task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusWaitingCategory", "Waiting for category selection..."));
                targetCharacter = await PromptUserForCharacterAsync(task.Mod?.Name, task.FileInfo?.FileName);

                if (targetCharacter == null)
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusInstallCancelledCached", "Installation Cancelled (Saved to cache)");
                        task.IsCompleted = true;
                        if (task.FileInfo != null) task.FileInfo.IsDownloading = false;
                        _ = SaveDownloadHistoryAsync();
                    });

                    var doneTitle = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/DownloadCompletedTitle", "Download Completed");
                    var doneMsgTpl = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/DownloadCompletedCachedMessage", "Mod downloaded to cache: {0}");
                    _notificationManager.ShowNotification(doneTitle, string.Format(doneMsgTpl, System.IO.Path.GetFileName(archivePath)), TimeSpan.FromSeconds(3));
                    return;
                }
            }

            _dispatcherQueue.TryEnqueue(() => task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusWaitingForInstall", "Waiting for other installs to complete..."));

            // Acquire the installation lock only when we actually begin data extraction & copying
            await _downloadQueueLock.WaitAsync(task.Cts?.Token ?? CancellationToken.None);
            lockAcquired = true;

            _dispatcherQueue.TryEnqueue(() => task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusInstalling2", "Installing..."));

            var modList = _skinManagerService.GetCharacterModList(targetCharacter);
            var cleanName = Path.GetFileNameWithoutExtension(task.FileInfo?.FileName ?? task.Mod?.Name ?? "mod");
            var modFolder = _archiveService.ExtractArchive(archivePath, App.GetUniqueTmpFolder().FullName, extractedFolderName: cleanName);
            var modUrl = !string.IsNullOrEmpty(task.ModUrl) ? new Uri(task.ModUrl) : null;

            try
            {
                using var installerTask = await _modInstallerService.StartModInstallationAsync(modFolder, modList,
                    setup: options => { options.ModUrl = modUrl; });

                var result = await installerTask.WaitForCloseAsync();

                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (result.CloseReason == CloseRequestedArgs.CloseReasons.Canceled)
                    {
                        task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusInstallCancelled", "Installation Cancelled");
                        task.IsCompleted = true;
                        if (task.FileInfo != null) task.FileInfo.IsDownloading = false;
                    }
                    else
                    {
                        task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusInstallSuccess", "Installation Successful");
                        task.IsCompleted = true;
                        task.ProgressPercentage = 100;
                        if (task.FileInfo != null) task.FileInfo.IsDownloading = false;
                    }
                    _ = SaveDownloadHistoryAsync();
                });
            }
            finally
            {
                if (modFolder.Exists)
                {
                    try { modFolder.Delete(true); } catch { }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusCancelled", "Cancelled");
                task.IsError = true;
                task.IsCompleted = true;
                if (task.FileInfo != null) task.FileInfo.IsDownloading = false;
                _ = SaveDownloadHistoryAsync();
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to download/install mod");
            _dispatcherQueue.TryEnqueue(() =>
            {
                var errPrefix = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusErrorPrefix", "Error: ");
                task.StatusMessage = $"{errPrefix}{ex.Message}";
                task.IsError = true;
                task.IsCompleted = true;
                if (task.FileInfo != null) task.FileInfo.IsDownloading = false;
                _ = SaveDownloadHistoryAsync();
            });
            var failTitle = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/DownloadFailedTitle", "Download/Installation Failed");
            _notificationManager.ShowNotification(failTitle, ex.Message, TimeSpan.FromSeconds(3));
        }
        finally
        {
            if (lockAcquired)
            {
                _downloadQueueLock.Release();
            }
        }
    }

    private Task<IModdableObject?> PromptUserForCharacterAsync(string? modName, string? fileName)
    {
        var tcs = new TaskCompletionSource<IModdableObject?>();
        _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var characters = _gameService.GetAllModdableObjects().ToList();
                if (characters.Count == 0)
                {
                    var failTitleInstall = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/InstallFailedTitle", "Installation Failed");
                    var failMsgNoChars = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/InstallFailedNoCharacters", "Failed to find character list, please ensure the game is properly initialized");
                    _notificationManager.ShowNotification(failTitleInstall, failMsgNoChars, TimeSpan.FromSeconds(5));
                    tcs.SetResult(null);
                    return;
                }

                var localizedCategories = _gameService.GetCategories()
                    .ToDictionary(c => c.InternalName.Id, c => c.DisplayName);

                var displayItems = characters
                    .Select(c =>
                    {
                        var catId = c.ModCategory?.InternalName?.Id ?? "Unknown";
                        var localizedName = localizedCategories.GetValueOrDefault(catId, catId);
                        return Tuple.Create(c, $"[{localizedName}] {c.DisplayName}");
                    })
                    .OrderBy(x => x.Item2)
                    .ToList();

                var dialog = new Views.Dialogs.GameBananaInstallTargetDialog(_localizer, _gameService, modName, fileName, displayItems);
                dialog.Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style;

                var result = await _windowManagerService.ShowDialogAsync(dialog);

                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                {
                    tcs.SetResult(dialog.SelectedTarget);
                }
                else
                {
                    tcs.SetResult(null);
                }
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}