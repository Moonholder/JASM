using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities.Mods.Helpers;
using GIMI_ModManager.Core.GamesService;
using GIMI_ModManager.Core.GamesService.Interfaces;
using GIMI_ModManager.Core.GamesService.Models;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.Core.Services.GameBanana;
using GIMI_ModManager.Core.Services.GameBanana.Models;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Services.AppManagement;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.Models.GameBanana;
using GIMI_ModManager.WinUI.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Serilog;

namespace GIMI_ModManager.WinUI.Services.ModHandling;

public class GameBananaDownloadSessionService : IGameBananaDownloadSessionService
{
    private readonly GameBananaCoreService _gbService;
    private readonly IGameService _gameService;
    private readonly ISkinManagerService _skinManagerService;
    private readonly ModInstallerService _modInstallerService;
    private readonly ImageHandlerService _imageHandlerService;
    private readonly ArchiveService _archiveService;
    private readonly NotificationManager _notificationManager;
    private readonly IWindowManagerService _windowManagerService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ElevatorService _elevatorService;
    private readonly UserPreferencesService _userPreferencesService;
    private readonly ILanguageLocalizer _localizer;
    private readonly ILogger _logger;
    private readonly DispatcherQueue _dispatcherQueue;

    public ObservableCollection<GbDownloadTask> DownloadQueue { get; } = new();
    private readonly SemaphoreSlim _downloadQueueLock = new(1, 1);
    private readonly SemaphoreSlim _historyLock = new(1, 1);
    private readonly SemaphoreSlim _categoryPromptDialogLock = new(1, 1);

    private readonly ConcurrentDictionary<Guid, byte> _pendingModsToSync = new();
    private CancellationTokenSource? _refreshDebounceCts;
    private readonly object _refreshDebounceLock = new();

    public GameBananaDownloadSessionService(
        GameBananaCoreService gbService,
        IGameService gameService,
        ISkinManagerService skinManagerService,
        ModInstallerService modInstallerService,
        ImageHandlerService imageHandlerService,
        ArchiveService archiveService,
        NotificationManager notificationManager,
        IWindowManagerService windowManagerService,
        ILocalSettingsService localSettingsService,
        ElevatorService elevatorService,
        UserPreferencesService userPreferencesService,
        ILanguageLocalizer localizer,
        ILogger logger)
    {
        _gbService = gbService;
        _gameService = gameService;
        _skinManagerService = skinManagerService;
        _modInstallerService = modInstallerService;
        _imageHandlerService = imageHandlerService;
        _archiveService = archiveService;
        _notificationManager = notificationManager;
        _windowManagerService = windowManagerService;
        _localSettingsService = localSettingsService;
        _elevatorService = elevatorService;
        _userPreferencesService = userPreferencesService;
        _localizer = localizer;
        _logger = logger.ForContext<GameBananaDownloadSessionService>();

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task LoadDownloadHistoryAsync()
    {
        try
        {
            if (DownloadQueue.Any()) return;

            var history = await _localSettingsService.ReadSettingAsync<List<DownloadHistoryEntry>>(GameBananaSettings.DownloadHistoryKey);
            if (history != null && history.Any())
            {
                var dict = new Dictionary<string, GbDownloadTask>();

                foreach (var entry in history)
                {
                    var task = new GbDownloadTask
                    {
                        Mod = new GbModDisplayItem
                        {
                            Name = entry.ModName,
                            ModelName = string.IsNullOrWhiteSpace(entry.ModelName) ? "Mod" : entry.ModelName,
                            AuthorName = entry.AuthorName
                        },
                        AuthorName = entry.AuthorName,
                        FileInfo = new ModFileInfo(!string.IsNullOrEmpty(entry.ModId) ? entry.ModId : "0", entry.FileId, entry.FileName, string.Empty, entry.Md5Checksum ?? string.Empty, DateTime.MinValue)
                        {
                            DownloadUrl = entry.DownloadUrl,
                            IsInstalled = entry.IsCompleted && !entry.IsError
                        },
                        CategoryName = entry.CategoryName,
                        ModUrl = entry.ModUrl,
                        PreviewImageUrl = entry.PreviewImageUrl,
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
                .TakeLast(20)
                .Select(t => new DownloadHistoryEntry
                {
                    ModId = t.FileInfo?.ModId ?? "",
                    CategoryName = t.CategoryName ?? "",
                    ModUrl = t.ModUrl ?? "",
                    PreviewImageUrl = t.PreviewImageUrl,
                    ModName = t.Mod?.Name ?? "",
                    AuthorName = t.AuthorName ?? t.Mod?.AuthorName ?? "",
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
            DownloadQueue.Remove(existingTask);
        }

        var cts = new CancellationTokenSource();
        fileInfo.IsDownloading = true;
        fileInfo.IsInstalling = false;
        fileInfo.IsInstalled = false;
        fileInfo.IsPaused = false;
        fileInfo.DownloadProgress = 0;
        fileInfo.DownloadStatus = "0%";

        var previewImageUrl = selectedModDetail?.PreviewImages?.FirstOrDefault()?.Original?.ToString()
            ?? selectedMod?.ThumbnailUrl;

        var author = selectedModDetail?.AuthorName ?? selectedMod?.AuthorName;

        var downloadTask = new GbDownloadTask
        {
            Mod = selectedMod!,
            FileInfo = fileInfo,
            CategoryName = selectedModDetail?.CategoryName,
            ModUrl = selectedModDetail?.ModPageUrl?.ToString(),
            PreviewImageUrl = previewImageUrl,
            AuthorName = author,
            Cts = cts
        };
        DownloadQueue.Add(downloadTask);

        var addedTitle = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/TaskAddedTitle", "Task Added to Queue");
        var addedMsgTpl = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/TaskAddedMessage", "Downloading {0} in background...");
        _notificationManager.ShowNotification(addedTitle, string.Format(addedMsgTpl, selectedMod?.Name), TimeSpan.FromSeconds(3));

        _ = Task.Run(() => ProcessDownloadTaskAsync(downloadTask));
    }

    public void PauseDownloadTask(GbDownloadTask? task)
    {
        if (task == null || task.IsCompleted || task.IsError || task.IsPaused || task.IsInstalling) return;

        task.IsPaused = true;
        if (task.FileInfo != null) task.FileInfo.IsPaused = true;
        task.Cts?.Cancel();
        _dispatcherQueue.TryEnqueue(() =>
        {
            task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusPaused", "Paused");
            if (task.FileInfo != null)
            {
                task.FileInfo.DownloadStatus = task.StatusMessage;
            }
            _ = SaveDownloadHistoryAsync();
        });
    }

    public void ResumeDownloadTask(GbDownloadTask? task)
    {
        if (task == null || task.IsCompleted || task.IsError || !task.IsPaused || task.IsInstalling) return;

        task.IsPaused = false;
        if (task.FileInfo != null) task.FileInfo.IsPaused = false;
        task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusPreparing", "Preparing...");
        task.Cts = new CancellationTokenSource();
        if (task.FileInfo != null)
        {
            task.FileInfo.IsDownloading = true;
            task.FileInfo.IsInstalling = false;
            task.FileInfo.DownloadStatus = task.StatusMessage;
        }

        _ = Task.Run(() => ProcessDownloadTaskAsync(task));
    }

    public void CancelDownloadTask(GbDownloadTask? task)
    {
        if (task is not { IsCompleted: false } || task.IsInstalling) return;
        task.Cts?.Cancel();
        _dispatcherQueue.TryEnqueue(() =>
        {
            task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusCancelled", "Cancelled");
            task.IsCompleted = true;
            task.IsError = true;
            task.IsInstalling = false;
            if (task.FileInfo != null)
            {
                task.FileInfo.IsDownloading = false;
                task.FileInfo.IsInstalling = false;
                task.FileInfo.IsPaused = false;
                task.FileInfo.DownloadProgress = 0;
                task.FileInfo.DownloadStatus = string.Empty;
            }
            _ = SaveDownloadHistoryAsync();
        });
    }

    public void CleanupDownloadAssets(GbDownloadTask task)
    {
        if (!string.IsNullOrEmpty(task.ArchivePath) && System.IO.File.Exists(task.ArchivePath))
        {
            try { File.Delete(task.ArchivePath); } catch { }
        }
        if (task.FileInfo != null && !string.IsNullOrWhiteSpace(task.FileInfo.ModId) && !string.IsNullOrWhiteSpace(task.FileInfo.FileId))
        {
            var identifier = new GbModFileIdentifier(new GbModId(task.FileInfo.ModId), new GbModFileId(task.FileInfo.FileId));
            _gbService.DeletePartialDownload(identifier);
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
        task.IsInstalling = false;
        task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusPreparing", "Preparing...");
        task.ProgressPercentage = 0;
        task.Cts = new CancellationTokenSource();

        _ = Task.Run(() => ProcessDownloadTaskAsync(task));
    }

    private async Task ProcessDownloadTaskAsync(GbDownloadTask task)
    {
        var cts = task.Cts;
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
                    if (task.FileInfo != null)
                    {
                        task.FileInfo.DownloadProgress = p;
                        task.FileInfo.DownloadStatus = $"{p}%";
                    }
                });
            });

            string archivePath;
            if (!string.IsNullOrWhiteSpace(task.FileInfo.DownloadUrl))
            {
                archivePath = await Task.Run(
                    () => _gbService.DownloadModByDirectUrlAsync(task.FileInfo, progress, cts?.Token ?? CancellationToken.None));
            }
            else
            {
                var identifier = new GbModFileIdentifier(
                    new GbModId(task.FileInfo.ModId), new GbModFileId(task.FileInfo.FileId));
                archivePath = await Task.Run(
                    () => _gbService.DownloadModAsync(identifier, task.Mod?.ModelName ?? "Mod", progress, cts?.Token ?? CancellationToken.None));
            }

            task.ArchivePath = archivePath;

            if (!ArchiveService.IsArchive(archivePath))
            {
                _dispatcherQueue.TryEnqueue(async () =>
                {
                    task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusDownloadToCache", "");
                    task.IsCompleted = true;
                    task.ProgressPercentage = 100;
                    if (task.FileInfo != null)
                    {
                        task.FileInfo.IsDownloading = false;
                        task.FileInfo.IsInstalling = false;
                        task.FileInfo.IsPaused = false;
                        task.FileInfo.DownloadProgress = 0;
                        task.FileInfo.DownloadStatus = string.Empty;
                    }
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

            IModdableObject? targetCharacter = _gameService.MatchExactModdableObject(task.CategoryName);
            if (targetCharacter == null)
            {
                var gbSettings = await _localSettingsService.ReadSettingAsync<GameBananaSettings>(GameBananaSettings.Key);
                if (gbSettings?.FallbackToOtherCategoryOnUnmatched == true)
                {
                    targetCharacter = _gameService.GetModdableObjectByIdentifier(new InternalName(_gameService.OtherCharacterInternalName));
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
                        if (task.FileInfo != null)
                        {
                            task.FileInfo.IsDownloading = false;
                            task.FileInfo.IsInstalling = false;
                            task.FileInfo.IsPaused = false;
                            task.FileInfo.DownloadProgress = 100;
                            task.FileInfo.DownloadStatus = task.StatusMessage;
                        }
                        _ = SaveDownloadHistoryAsync();
                    });

                    var doneTitle = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/DownloadCompletedTitle", "Download Completed");
                    var doneMsgTpl = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/DownloadCompletedCachedMessage", "Mod downloaded to cache: {0}");
                    _notificationManager.ShowNotification(doneTitle, string.Format(doneMsgTpl, System.IO.Path.GetFileName(archivePath)), TimeSpan.FromSeconds(3));
                    return;
                }
            }

            _dispatcherQueue.TryEnqueue(() => task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusWaitingForInstall", "Waiting for other installs to complete..."));

            await _downloadQueueLock.WaitAsync(cts?.Token ?? CancellationToken.None);
            lockAcquired = true;

            _dispatcherQueue.TryEnqueue(() =>
            {
                task.IsInstalling = true;
                task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusInstalling2", "Installing...");
                if (task.FileInfo != null)
                {
                    task.FileInfo.IsInstalling = true;
                    task.FileInfo.DownloadStatus = task.StatusMessage;
                }
            });

            var modList = _skinManagerService.GetCharacterModList(targetCharacter);
            var rawName = Path.GetFileNameWithoutExtension(task.FileInfo?.FileName ?? task.Mod?.Name ?? "mod");
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleanName = string.Concat(rawName.Select(c => invalidChars.Contains(c) ? '_' : c)).Trim();
            if (string.IsNullOrWhiteSpace(cleanName)) cleanName = "mod";
            var modFolder = _archiveService.ExtractArchive(archivePath, App.GetUniqueTmpFolder().FullName, extractedFolderName: cleanName);

            try
            {
                var gbSettings = await _localSettingsService.ReadSettingAsync<GameBananaSettings>(GameBananaSettings.Key);
                var autoEnable = gbSettings?.AutoEnableAfterSilentInstall ?? true;

                Uri? modImageUri = null;
                if (!string.IsNullOrEmpty(task.PreviewImageUrl) && Uri.TryCreate(task.PreviewImageUrl, UriKind.Absolute, out var remoteUri))
                {
                    try
                    {
                        var downloadedImage = await _imageHandlerService.DownloadImageAsync(remoteUri, cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
                        if (downloadedImage != null && !string.IsNullOrEmpty(downloadedImage.Path))
                        {
                            modImageUri = new Uri(downloadedImage.Path);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Failed to download remote preview image {Url}", task.PreviewImageUrl);
                    }
                }

                if (modImageUri == null)
                {
                    var localImages = SkinModHelpers.DetectModPreviewImages(modFolder.FullName);
                    if (localImages.Any())
                    {
                        modImageUri = localImages.First();
                    }
                }

                var author = !string.IsNullOrWhiteSpace(task.AuthorName)
                    ? task.AuthorName
                    : task.Mod?.AuthorName;

                if (string.IsNullOrWhiteSpace(author) &&
                    !string.IsNullOrWhiteSpace(task.FileInfo?.ModId) &&
                    int.TryParse(task.FileInfo.ModId, out var parsedModId) &&
                    parsedModId > 0)
                {
                    try
                    {
                        var profile = await _gbService.GetModProfileAsync(new GbModId(task.FileInfo.ModId), task.Mod?.ModelName ?? "Mod", cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
                        author = profile?.AuthorName;
                        if (!string.IsNullOrEmpty(author))
                        {
                            task.AuthorName = author;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Could not fetch author profile for mod ID {ModId}", task.FileInfo.ModId);
                    }
                }

                var addOptions = new AddModOptions
                {
                    ModName = task.Mod?.Name,
                    ModUrl = task.ModUrl,
                    Author = author,
                    Description = task.FileInfo?.Description,
                    ModImage = modImageUri
                };

                var installResult = await _modInstallerService.InstallSilentlyAsync(modFolder, modList, addOptions, autoEnable);
                var installedMod = installResult.InstalledMod;

                bool archiveDeleted = false;
                if ((gbSettings?.DeleteArchiveAfterInstall ?? true) && !string.IsNullOrEmpty(archivePath))
                {
                    try
                    {
                        archiveDeleted = _gbService.DeleteArchive(archivePath);
                        if (archiveDeleted)
                        {
                            task.ArchivePath = string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Failed to delete archive after installation: {ArchivePath}", archivePath);
                    }
                }

                _dispatcherQueue.TryEnqueue(() =>
                {
                    task.StatusMessage = archiveDeleted
                        ? _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusInstallSuccessCleaned", "Installed (Archive cleaned)")
                        : _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusInstallSuccess", "Installation Successful");
                    task.IsInstalling = false;
                    task.IsCompleted = true;
                    task.ProgressPercentage = 100;
                    if (task.FileInfo != null)
                    {
                        task.FileInfo.IsDownloading = false;
                        task.FileInfo.IsInstalling = false;
                        task.FileInfo.IsInstalled = true;
                        task.FileInfo.IsPaused = false;
                        task.FileInfo.DownloadProgress = 100;
                        task.FileInfo.DownloadStatus = task.StatusMessage;
                    }
                    _ = SaveDownloadHistoryAsync();
                });

                if (autoEnable)
                {
                    ScheduleBatchRefresh(installResult.DisabledModIds);
                }

                var installSuccessTitle = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/InstallSuccessTitle", "Installation Successful");
                var installSuccessMsgTpl = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/InstallSuccessMessage", "\"{0}\" has been installed to {1}");
                _notificationManager.ShowNotification(
                    installSuccessTitle,
                    string.Format(installSuccessMsgTpl, installedMod.Name, targetCharacter.DisplayName),
                    TimeSpan.FromSeconds(3));
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
                task.IsInstalling = false;
                if (task.IsPaused)
                {
                    task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusPaused", "Paused");
                    if (task.FileInfo != null)
                    {
                        task.FileInfo.IsPaused = true;
                        task.FileInfo.IsInstalling = false;
                        task.FileInfo.DownloadStatus = task.StatusMessage;
                    }
                    return;
                }

                task.StatusMessage = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/StatusCancelled", "Cancelled");
                task.IsError = true;
                task.IsCompleted = true;
                if (task.FileInfo != null)
                {
                    task.FileInfo.IsDownloading = false;
                    task.FileInfo.IsInstalling = false;
                    task.FileInfo.IsPaused = false;
                    task.FileInfo.DownloadProgress = 0;
                    task.FileInfo.DownloadStatus = string.Empty;
                }
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
                task.IsInstalling = false;
                if (task.FileInfo != null)
                {
                    task.FileInfo.IsDownloading = false;
                    task.FileInfo.IsInstalling = false;
                    task.FileInfo.IsPaused = false;
                    task.FileInfo.DownloadProgress = 0;
                    task.FileInfo.DownloadStatus = string.Empty;
                }
                _ = SaveDownloadHistoryAsync();
            });
            var failTitle = _localizer.GetLocalizedStringOrDefault("/GameBananaPage/DownloadFailedTitle", "Download/Installation Failed");
            var errorMsg = !string.IsNullOrWhiteSpace(ex.Message) ? ex.Message : ex.GetType().Name;
            _notificationManager.ShowNotification(failTitle, errorMsg, TimeSpan.FromSeconds(3));
        }
        finally
        {
            if (lockAcquired)
            {
                _downloadQueueLock.Release();
            }

            if (cts != null && ReferenceEquals(task.Cts, cts))
            {
                task.Cts = null;
                cts.Dispose();
            }
        }
    }

    private void ScheduleBatchRefresh(IEnumerable<Guid> disabledModIds)
    {
        foreach (var id in disabledModIds)
        {
            _pendingModsToSync.TryAdd(id, 0);
        }

        lock (_refreshDebounceLock)
        {
            _refreshDebounceCts?.Cancel();
            _refreshDebounceCts?.Dispose();
            _refreshDebounceCts = new CancellationTokenSource();
            var token = _refreshDebounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(800, token).ConfigureAwait(false);

                    while (DownloadQueue.Any(t => t.IsInstalling || (!t.IsCompleted && !t.IsError && !t.IsPaused)))
                    {
                        await Task.Delay(500, token).ConfigureAwait(false);
                    }

                    var modPresetSetting = await _localSettingsService.ReadOrCreateSettingAsync<ModPresetSettings>(ModPresetSettings.Key).ConfigureAwait(false);
                    bool autoSyncRunning = modPresetSetting.AutoSyncMods && _elevatorService.CheckStatus() == ElevatorStatus.Running;

                    if (autoSyncRunning)
                    {
                        try
                        {
                            await Task.Delay(500, token).ConfigureAwait(false);
                            await _elevatorService.RefreshGenshinMods().ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to refresh 3Dmigoto during batch mod installation");
                        }
                    }

                    var modsToSync = _pendingModsToSync.Keys.ToArray();
                    _pendingModsToSync.Clear();

                    if (modsToSync.Length > 0)
                    {
                        await Task.Delay(50, token).ConfigureAwait(false);
                        await Parallel.ForEachAsync(modsToSync, token, async (modId, ct) =>
                        {
                            try
                            {
                                await _userPreferencesService.SyncPreferencesToModLocalFilesAsync(modId).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Failed to sync preferences for mod ID {ModId}", modId);
                            }
                        }).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during batch refresh coordination");
                }
            }, token);
        }
    }

    private async Task<IModdableObject?> PromptUserForCharacterAsync(string? modName, string? fileName)
    {
        await _categoryPromptDialogLock.WaitAsync().ConfigureAwait(false);
        try
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
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _categoryPromptDialogLock.Release();
        }
    }
}