using Windows.Storage;
using Windows.Win32;
using Windows.Win32.Media.Audio;
using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.WinUI.Services.AppManagement;
using Serilog;
using GIMI_ModManager.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Microsoft.UI.Dispatching;
using System.Threading.Tasks;
using static GIMI_ModManager.WinUI.Services.ModHandling.ModDragAndDropService.DragAndDropFinishedArgs;

namespace GIMI_ModManager.WinUI.Services.ModHandling;

public class ModDragAndDropService
{
    private readonly ILogger _logger;
    private readonly ModInstallerService _modInstallerService;
    private readonly IWindowManagerService _windowManagerService;


    private readonly Notifications.NotificationManager _notificationManager;

    public event EventHandler<DragAndDropFinishedArgs>? DragAndDropFinished;

    public ModDragAndDropService(ILogger logger, Notifications.NotificationManager notificationManager,
        ModInstallerService modInstallerService, IWindowManagerService windowManagerService)
    {
        _notificationManager = notificationManager;
        _modInstallerService = modInstallerService;
        _windowManagerService = windowManagerService;
        _logger = logger.ForContext<ModDragAndDropService>();
    }

    // Drag and drop directly from 7zip is REALLY STRANGE, I don't know why 7zip 'usually' deletes the files before we can copy them
    // Sometimes only a few folders are copied, sometimes only a single file is copied, but usually 7zip removes them and the app just crashes
    // This code is a mess, but it works.
    public async Task<InstallMonitor?> AddStorageItemFoldersAsync(
        ICharacterModList modList, IReadOnlyList<IStorageItem>? storageItems)
    {
        if (storageItems is null || !storageItems.Any())
        {
            _logger.Warning("Drag and drop files called with null/0 storage items.");
            return null;
        }


        if (storageItems.Count > 1)
        {
            _notificationManager.ShowNotification(
                "Drag and drop called with more than one storage item, this is currently not supported", "",
                TimeSpan.FromSeconds(5));
            return null;
        }

        if (_windowManagerService.GetWindow(modList) is { } window)
        {
            _notificationManager.ShowNotification(
                $"Please finish adding the mod for '{modList.Character.DisplayName}' first",
                $"JASM does not support multiple mod installs for the same character",
                TimeSpan.FromSeconds(8));

            PInvoke.PlaySound("SystemAsterisk", null,
                SND_FLAGS.SND_ASYNC | SND_FLAGS.SND_ALIAS | SND_FLAGS.SND_NODEFAULT);

            App.MainWindow.DispatcherQueue.TryEnqueue(() => window.Activate());
            return null;
        }

        var storageItem = storageItems.FirstOrDefault();

        InstallMonitor? installMonitor;
        if (storageItem is StorageFile)
        {
            var scanner = new DragAndDropScanner();
            var extractResult = scanner.ScanAndGetContents(storageItem.Path);
            if (extractResult.exitedCode == 1 || extractResult.exitedCode == 2)
            {
                extractResult = await ShowPasswordInputWindow(scanner, storageItem.Path);
                if (extractResult?.exitedCode == 2)
                {
                    _notificationManager.ShowNotification(
                        "解压失败",
                        "密码错误，请尝试重新添加模组",
                        TimeSpan.FromSeconds(5));
                    return null;
                }
            }

            if (extractResult != null)
            {
                installMonitor = await _modInstallerService.StartModInstallationAsync(
                    new DirectoryInfo(extractResult.ExtractedFolder.FullPath), modList);
                return installMonitor;
            }
        }

        if (storageItem is not StorageFolder sourceFolder)
        {
            _logger.Information("Unknown storage item type from drop: {StorageItemType}", storageItem.GetType());
            return null;
        }

        var destDirectoryInfo = App.GetUniqueTmpFolder();
        destDirectoryInfo.Create();
        destDirectoryInfo = new DirectoryInfo(Path.Combine(destDirectoryInfo.FullName, storageItem.Name));


        _logger.Debug("Source destination folder for drag and drop: {Source}", sourceFolder.Path);
        _logger.Debug("Copying folder {FolderName} to {DestinationFolder}", sourceFolder.Path,
            destDirectoryInfo.FullName);


        var sourceFolderPath = sourceFolder.Path;


        if (sourceFolderPath is null)
        {
            _logger.Warning("Source folder path is null, skipping.");
            return null;
        }

        var tmpFolder = Path.GetTempPath();

        Action<StorageFolder, StorageFolder> recursiveCopy = null!;

        if (sourceFolderPath.Contains(tmpFolder)) // Is 7zip
        {
            destDirectoryInfo = new DirectoryInfo(Path.Combine(destDirectoryInfo.FullName, sourceFolder.Name));
            recursiveCopy = RecursiveCopy7z;
        }
        else
        {
            destDirectoryInfo = new DirectoryInfo(Path.Combine(destDirectoryInfo.FullName, sourceFolder.Name));
            recursiveCopy = RecursiveCopy;
        }

        destDirectoryInfo.Create();

        try
        {
            recursiveCopy.Invoke(sourceFolder,
                await StorageFolder.GetFolderFromPathAsync(destDirectoryInfo.FullName));
        }
        catch (Exception)
        {
            Directory.Delete(destDirectoryInfo.FullName);
            throw;
        }

        installMonitor = await _modInstallerService.StartModInstallationAsync(destDirectoryInfo.Parent!, modList)
            .ConfigureAwait(false);
        DragAndDropFinished?.Invoke(this, new DragAndDropFinishedArgs(new List<ExtractPaths>()));
        return installMonitor;
    }

    private async Task<DragAndDropScanResult> ShowPasswordInputWindow(DragAndDropScanner scanner, string filePath)
    {
        var tcs = new TaskCompletionSource<DragAndDropScanResult>();

        // 创建一个ContentDialog
        var passwordDialog = new ContentDialog
        {
            Title = "输入加密文件的密码",
            PrimaryButtonText = "确认",
            SecondaryButtonText = "取消"
        };

        // 创建一个Border并设置宽高
        var border = new Border
        {
            Width = 400,
            Height = 200,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
        };

        // 创建一个Frame并导航到PasswordInputPage
        var frame = new Frame();
        frame.Navigate(typeof(PasswordInputPage));

        // 获取导航到的页面实例
        var passwordPage = frame.Content as PasswordInputPage;

        // 设置Border的内容为Frame
        border.Child = frame;

        // 设置对话框的内容为Border
        passwordDialog.Content = border;

        // 获取当前窗口的XamlRoot
        var xamlRoot = App.MainWindow.Content.XamlRoot;
        if (xamlRoot != null)
        {
            passwordDialog.XamlRoot = xamlRoot;
        }

        // 处理确认按钮点击事件
        passwordDialog.PrimaryButtonClick += (sender, args) =>
        {
            // 获取密码
            var password = passwordPage?.GetPassword();

            // 在这里使用密码进行后续操作，例如重新尝试解压
            var extractResult = scanner.ScanAndGetContents(filePath, password);
            tcs.SetResult(extractResult);
            passwordDialog.Hide();
        };

        // 处理取消按钮点击事件
        passwordDialog.SecondaryButtonClick += (sender, args) =>
        {
            tcs.SetResult(null); // 返回null表示取消操作
            passwordDialog.Hide();
        };

        // 显示对话框
        await passwordDialog.ShowAsync();

        return await tcs.Task;
    }

    // ReSharper disable once InconsistentNaming
    private void RecursiveCopy7z(StorageFolder sourceFolder, StorageFolder destinationFolder)
    {
        var tmpFolder = Path.GetTempPath();
        var parentDir = new DirectoryInfo(Path.GetDirectoryName(sourceFolder.Path)!);
        parentDir.MoveTo(Path.Combine(tmpFolder, "JASM_TMP", Guid.NewGuid().ToString("N")));

        var modDir = parentDir.EnumerateDirectories().FirstOrDefault();

        if (modDir is null)
        {
            throw new DirectoryNotFoundException("No valid mod folder found in archive. Loose files are ignored");
        }

        RecursiveCopy(StorageFolder.GetFolderFromPathAsync(modDir.FullName).GetAwaiter().GetResult(),
            destinationFolder);
    }

    private void RecursiveCopy(StorageFolder sourceFolder, StorageFolder destinationFolder)
    {
        if (sourceFolder == null || destinationFolder == null)
            throw new ArgumentNullException("Source and destination folders cannot be null.");

        var sourceDir = new DirectoryInfo(sourceFolder.Path);

        // Copy files
        foreach (var file in sourceDir.GetFiles())
        {
            _logger.Debug("Copying file {FileName} to {DestinationFolder}", file.FullName, destinationFolder.Path);
            if (!File.Exists(file.FullName))
            {
                _logger.Warning("File {FileName} does not exist.", file.FullName);
                continue;
            }

            file.CopyTo(Path.Combine(destinationFolder.Path, file.Name), true);
        }
        // Recursively copy subfolders

        foreach (var subFolder in sourceDir.GetDirectories())
        {
            _logger.Debug("Copying subfolder {SubFolderName} to {DestinationFolder}", subFolder.FullName,
                destinationFolder.Path);
            if (!Directory.Exists(subFolder.FullName))
            {
                _logger.Warning("Subfolder {SubFolderName} does not exist.", subFolder.FullName);
                continue;
            }

            var newSubFolder = new DirectoryInfo(Path.Combine(destinationFolder.Path, subFolder.Name));
            newSubFolder.Create();
            RecursiveCopy(StorageFolder.GetFolderFromPathAsync(subFolder.FullName).GetAwaiter().GetResult(),
                StorageFolder.GetFolderFromPathAsync(newSubFolder.FullName).GetAwaiter().GetResult());
        }
    }


    public async Task AddModFromUrlAsync(ICharacterModList modList, Uri uri)
    {
        var windowKey = $"ModPage_{modList.Character.InternalName}";
        if (_windowManagerService.GetWindow(windowKey) is { } window)
        {
            PInvoke.PlaySound("SystemAsterisk", null,
                SND_FLAGS.SND_ASYNC | SND_FLAGS.SND_ALIAS | SND_FLAGS.SND_NODEFAULT);

            App.MainWindow.DispatcherQueue.TryEnqueue(() => window.Activate());
            return;
        }


        var modWindow = new GbModPageWindow(uri, modList.Character);
        _windowManagerService.CreateWindow(modWindow, identifier: windowKey);
        await Task.Delay(100);
        modWindow.BringToFront();
    }

    public class DragAndDropFinishedArgs : EventArgs
    {
        public DragAndDropFinishedArgs(IReadOnlyCollection<ExtractPaths> extractResults)
        {
            ExtractResults = extractResults;
        }

        public IReadOnlyCollection<ExtractPaths> ExtractResults { get; }

        public record ExtractPaths
        {
            public ExtractPaths(string sourcePath, string extractedFolderPath)
            {
                SourcePath = sourcePath;
                ExtractedFolderPath = Path.EndsInDirectorySeparator(extractedFolderPath)
                    ? extractedFolderPath
                    : extractedFolderPath + Path.DirectorySeparatorChar;
            }

            public string SourcePath { get; init; }
            public string ExtractedFolderPath { get; init; }

            public void Deconstruct(out string SourcePath, out string ExtractedFolderPath)
            {
                SourcePath = this.SourcePath;
                ExtractedFolderPath = this.ExtractedFolderPath;
            }
        }
    }
}