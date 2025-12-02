using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JASM.AutoUpdater.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;
using static MirrorAddressSelector;

namespace JASM.AutoUpdater;

public partial class MainPageVM : ObservableRecipient
{
    private readonly string WorkDir = Path.Combine(Path.GetTempPath(), "JASM_Auto_Updater");
    private string _zipPath = string.Empty;
    private DirectoryInfo _extractedJasmFolder = null!;
    private DirectoryInfo _installedJasmFolder = null!;
    private string _newJasmExePath = string.Empty;

    private readonly string _7zPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Assets\7z\", "7z.exe");

    [ObservableProperty] private bool _inStartupView = true;

    [ObservableProperty] private bool _updateProcessStarted = false;
    [ObservableProperty] private string _latestVersion = "-----";
    [ObservableProperty] private Uri _defaultBrowserUri = new("https://github.com/Jorixon/JASM/releases");

    public ObservableCollection<LogEntry> ProgressLog { get; } = new();

    public UpdateProgress UpdateProgress { get; } = new();

    public Version InstalledVersion { get; }
    [NotifyPropertyChangedFor(nameof(CanStartUpdate))]
    [NotifyCanExecuteChangedFor(nameof(StartUpdateCommand))]
    [ObservableProperty] private bool _isLoading = false;
    [NotifyPropertyChangedFor(nameof(ShowCancelButton), nameof(ShowRetryButton))]
    [ObservableProperty] private bool _finishedSuccessfully = false;
    [NotifyPropertyChangedFor(nameof(ShowCancelButton), nameof(ShowRetryButton))]
    [ObservableProperty] private bool _stopped;
    [ObservableProperty] private string? _stopReason;
    [ObservableProperty] private bool _enableMirrorAcceleration = true;
    [ObservableProperty] private MirrorInfo _currentMirror;
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadSpeed = "0 KB/s";
    [ObservableProperty] private string _downloadStatus = "等待下载...";
    [ObservableProperty] private string _fileSize = "";

    private long _totalBytes;
    private long _bytesReceived;
    private Stopwatch _downloadStopwatch;
    private HttpClient _httpClient;


    private bool RetryMirrorAcceleration = false;
    public MainPageVM() { }
    public MainPageVM(string installedJasmVersion)
    {
        InstalledVersion = Version.TryParse(installedJasmVersion, out var version) ? version : new Version(0, 0, 0, 0);
        // 监听UpdateProgress的属性变化
        UpdateProgress.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(UpdateProgress.DownloadingLatestUpdate))
            {
                OnPropertyChanged(nameof(ShowDownloadProgress));
            }
        };

        InitializeAsync().ConfigureAwait(false);
    }

    public bool ShowDownloadProgress => UpdateProgress.DownloadingLatestUpdate && !Stopped;

    // 在Stopped属性变化时触发ShowDownloadProgress的更新
    partial void OnStoppedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDownloadProgress));
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        CurrentMirror = await GetBestMirrorAsync();
        IsLoading = false;
    }

    public bool CanStartUpdate => !IsLoading;

    public bool ShowCancelButton => !Stopped && !FinishedSuccessfully;

    public bool ShowRetryButton => Stopped && !FinishedSuccessfully;

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanStartUpdate))]
    private async Task StartUpdateAsync(CancellationToken cancellationToken)
    {
        UpdateProgress.Reset();
        IsLoading = true;
        InStartupView = false;
        UpdateProcessStarted = true;
        Stopped = false;
        StopReason = null;

        Log(InstalledVersion.Equals(new Version(0, 0, 0, 0))
            ? "无法确定安装的JASM版本..."
            : $"已安装的JASM版本: {InstalledVersion}");


        try
        {
            if (RetryMirrorAcceleration)
            {
                CurrentMirror = MirrorAddressSelector.GetNextMirror();
                RetryMirrorAcceleration = false;
                Log($"已切换到镜像节点: {CurrentMirror.NodeName}");
            }

            var release = await IsNewerVersionAvailable(cancellationToken);
            UpdateProgress.NextStage();
            if (Stopped || release is null)
                return;

            await Task.Delay(1000, cancellationToken);
            UpdateProgress.NextStage();
            await DownloadLatestVersion(release, cancellationToken);

            if (Stopped)
            {
                CleanUp();
                return;
            }

            await Task.Delay(1000, cancellationToken);
            await UnzipLatestVersion(cancellationToken);
            UpdateProgress.NextStage();

            if (Stopped)
            {
                CleanUp();
                return;
            }

            await Task.Delay(1000, cancellationToken);
            await InstallLatestVersion();
            if (Stopped)
            {
                CleanUp();
                return;
            }

            UpdateProgress.NextStage();
        }
        catch (TaskCanceledException e)
        {
            Stop("用户取消");
            RetryMirrorAcceleration = true;
        }
        catch (OperationCanceledException e)
        {
            Stop("用户取消");
        }
        catch (Exception e)
        {
            Log("发生错误!", e.Message);
            Serilog.Log.Error(e, "发生错误！完整错误信息");
            Stop(e.Message);
        }
        finally
        {
            IsLoading = false;
        }

        if (Stopped)
        {
            CleanUp();
            return;
        }

        CleanUp();
        Finish();
    }

    [RelayCommand]
    private async Task SwitchMirror()
    {
        IsLoading = true;
        try
        {
            CurrentMirror = MirrorAddressSelector.GetNextMirror();
            await MirrorAddressSelector.TestMirrorAsync(CurrentMirror);
            Log($"已切换到镜像节点: {CurrentMirror.NodeName}");
        }
        catch (Exception e)
        {
            Log("切换镜像节点失败!", e.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void Finish()
    {
        IsLoading = false;
        FinishedSuccessfully = true;
    }

    private async Task<GitHubRelease?> IsNewerVersionAvailable(CancellationToken cancellationToken)
    {
        var newestVersionFound = await GetLatestVersionAsync(cancellationToken);

        Log($"找到最新版本: {newestVersionFound?.TagName}");

        var release = new GitHubRelease()
        {
            Version = new Version(newestVersionFound?.TagName?.Trim('v') ?? ""),
            PreRelease = newestVersionFound?.Prerelease ?? false,
            PublishedAt = newestVersionFound?.PublishedAt ?? DateTime.MinValue
        };

        if (release.Version <= InstalledVersion)
        {
            Stop("当前版本比GitHub上的最新版本更新或等于最新版本");
            return null;
        }

        var getJasmAsset = newestVersionFound?.Assets?.FirstOrDefault(a => a.Name?.StartsWith("JASM_") ?? false);

        if (getJasmAsset?.BrowserDownloadUrl is null)
        {
            Stop(
                "在GitHub上的最新版本中找不到JASM存档。这可能是由于开发人员必须手动上传zip，这可能需要几分钟. " +
                "如果问题仍然存在，那么您可能必须手动更新JASM");
            return null;
        }

        release.DownloadUrl = EnableMirrorAcceleration ? new Uri(CurrentMirror.Address + getJasmAsset.BrowserDownloadUrl) : new Uri(getJasmAsset.BrowserDownloadUrl);
        release.BrowserUrl = new Uri(newestVersionFound?.HtmlUrl ?? "https://github.com/Moonholder/JASM/releases");
        release.FileName = getJasmAsset.Name ?? "JASM.zip";

        LatestVersion = release.Version.ToString();

        DefaultBrowserUri = release.BrowserUrl;

        return release;
    }

    public void Stop(string stopReason)
    {
        Stopped = true;
        StopReason = stopReason;
    }

    private async Task DownloadLatestVersion(GitHubRelease gitHubRelease, CancellationToken cancellationToken)
    {
        UpdateProgress.DownloadingLatestUpdate = true;
        if (Directory.Exists(WorkDir))
        {
            Directory.Delete(WorkDir, true);
        }
        Directory.CreateDirectory(WorkDir);

        _zipPath = Path.Combine(WorkDir, gitHubRelease.FileName);
        if (File.Exists(_zipPath))
        {
            File.Delete(_zipPath);
        }

        _httpClient = CreateHttpClient();
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/octet-stream");

        Log("正在下载最新版本...");

        // 重置下载状态
        DownloadProgress = 0;
        DownloadSpeed = "0 KB/s";
        DownloadStatus = "准备下载...";
        _bytesReceived = 0;
        _totalBytes = 0;
        _downloadStopwatch = Stopwatch.StartNew();

        UpdateDownloadProgress();

        try
        {
            var result = await _httpClient.GetAsync(gitHubRelease.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!result.IsSuccessStatusCode)
            {
                if (EnableMirrorAcceleration)
                {
                    RetryMirrorAcceleration = true;
                    Stop($"下载最新版本失败. 状态码: {result.StatusCode}, 原因: 当前镜像节点 [{CurrentMirror.NodeName}] 可能已失效,重试自动切换其他节点");
                }
                else
                {
                    Stop($"下载最新版本失败. 状态码: {result.StatusCode}, 原因: {result.ReasonPhrase}");
                }
                return;
            }

            // 获取文件大小
            _totalBytes = result.Content.Headers.ContentLength ?? 0;
            FileSize = _totalBytes > 0 ? FormatFileSize(_totalBytes) : "未知大小";

            await using var stream = await result.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = File.Create(_zipPath);

            var buffer = new byte[8192];
            int bytesRead;
            var lastUpdateTime = DateTime.Now;

            DownloadStatus = "下载中...";

            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);

                _bytesReceived += bytesRead;

                // 每500毫秒更新一次UI状态
                if ((DateTime.Now - lastUpdateTime).TotalMilliseconds > 500)
                {
                    UpdateDownloadProgress();
                    lastUpdateTime = DateTime.Now;
                }
            }

            // 更新进度
            UpdateDownloadProgress();
            _downloadStopwatch.Stop();

            Log($"从 {gitHubRelease.DownloadUrl} 下载最新版本成功.");
            DownloadStatus = "下载完成";
            DownloadProgress = 100;
            await Task.Delay(500, cancellationToken);
        }
        catch (Exception)
        {
            _downloadStopwatch?.Stop();
            DownloadStatus = "下载失败";
            throw;
        }
        finally
        {
            if (Stopped)
            {
                DownloadStatus = "下载已取消";
            }
        }
    }

    private void UpdateDownloadProgress()
    {
        if (_totalBytes > 0)
        {
            DownloadProgress = (_bytesReceived / (double)_totalBytes) * 100;
        }

        // 计算下载速度
        var elapsedSeconds = _downloadStopwatch.Elapsed.TotalSeconds;
        if (elapsedSeconds > 0)
        {
            var bytesPerSecond = _bytesReceived / elapsedSeconds;
            DownloadSpeed = FormatSpeed(bytesPerSecond);
        }

        // 更新状态信息
        DownloadStatus = $"{FormatFileSize(_bytesReceived)} / {FileSize}";
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double len = bytes;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
        {
            return $"{bytesPerSecond / (1024 * 1024):0.##} MB/s";
        }
        else if (bytesPerSecond >= 1024)
        {
            return $"{bytesPerSecond / 1024:0.##} KB/s";
        }
        else
        {
            return $"{bytesPerSecond:0} B/s";
        }
    }

    private async Task UnzipLatestVersion(CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo =
            {
                FileName = _7zPath,
                Arguments = $"x \"{_zipPath}\" -o\"{WorkDir}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        Log("正在解压下载文件...");
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            Stop($"解压下载文件失败. 退出代码: {process.ExitCode}");
            return;
        }


        _extractedJasmFolder = new DirectoryInfo(WorkDir).EnumerateDirectories().FirstOrDefault(folder =>
            folder.Name.StartsWith("JASM", StringComparison.CurrentCultureIgnoreCase))!;

        if (_extractedJasmFolder is null)
        {
            Stop("未能在解压的 zip 文件中找到 JASM 文件夹");
            return;
        }

        Log($"JASM 应用程序文件夹已成功解压。路径: {_extractedJasmFolder.FullName}");
    }

    private async Task InstallLatestVersion()
    {
        _installedJasmFolder = new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory)).Parent!;
        if (_installedJasmFolder is null)
        {
            Stop("未能在路径中找到已安装的 JASM 文件夹");
            return;
        }

        const string jasmExe = "JASM - Just Another Skin Manager.exe";
        _newJasmExePath = Path.Combine(_installedJasmFolder.FullName, jasmExe);

        var containsJasmExe = false;
        var containsSystemFiles = false;
        var systemFileFound = string.Empty;
        var warningFiles = new List<string>();

        foreach (var fileSystemInfo in _installedJasmFolder.EnumerateFileSystemInfos())
        {
            if (fileSystemInfo.Name.Equals(jasmExe,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                containsJasmExe = true;
            }

            if (SystemEntries.WindowsEntries.Any(fileEntry => fileSystemInfo.Name.Equals(fileEntry,
                    StringComparison.CurrentCultureIgnoreCase)))
            {
                containsSystemFiles = true;
                systemFileFound = fileSystemInfo.Name;
            }

            if (fileSystemInfo.Name.Equals("3DMigoto Loader.exe", StringComparison.CurrentCultureIgnoreCase))
            {
                warningFiles.Add(fileSystemInfo.Name);
            }

            if (fileSystemInfo.Name.Equals("3dmigoto", StringComparison.CurrentCultureIgnoreCase))
            {
                warningFiles.Add(fileSystemInfo.Name);
            }

            if (fileSystemInfo.Name.Equals("Mods", StringComparison.CurrentCultureIgnoreCase))
            {
                warningFiles.Add("Mods");
            }
        }

        if (!containsJasmExe)
        {
            Stop(
                $"未能在已安装的 JASM 文件夹中找到 '{jasmExe}'。路径: {_installedJasmFolder}");
            return;
        }

        if (containsSystemFiles)
        {
            Stop(
                $"JASM 文件夹似乎包含 Windows 系统文件，这绝不应该发生。找到的文件：'{systemFileFound}' 在" +
                $"路径: {_installedJasmFolder}");
            return;
        }

        var result = await ShowDeleteWarning(warningFiles);

        if (result is ContentDialogResult.Secondary or ContentDialogResult.None)
        {
            Stop("用户取消");
            return;
        }

        var autoUpdaterFolder = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

        Log("正在删除旧文件...", $"路径: {_installedJasmFolder.FullName}");

        string[] doNotDeleteFiles = ["Elevator.exe", "JASM - Just Another Skin Manager.exe.WebView2", "logs"];

        foreach (var fileSystemInfo in _installedJasmFolder.EnumerateFileSystemInfos())
        {
            if (fileSystemInfo.Name.StartsWith(autoUpdaterFolder.Name,
                    StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            if (doNotDeleteFiles.Any(fileEntry => fileSystemInfo.Name.Equals(fileEntry,
                    StringComparison.CurrentCultureIgnoreCase)))
            {
                Serilog.Log.Logger.Information("Not deleting file: {FileName}", fileSystemInfo.Name);
                continue;
            }

            if (fileSystemInfo is DirectoryInfo directoryInfo)
                directoryInfo.Delete(true);
            else
                fileSystemInfo.Delete();
        }

        Log("复制新文件...", $"Path: {_installedJasmFolder.FullName}");

        await Task.Run(() => { CopyFilesRecursively(_extractedJasmFolder, _installedJasmFolder); });

        Log("JASM更新成功");
    }

    // https://stackoverflow.com/questions/58744/copy-the-entire-contents-of-a-directory-in-c-sharp
    private static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target)
    {
        foreach (var dir in source.GetDirectories())
            CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));
        foreach (var file in source.GetFiles())
            file.CopyTo(Path.Combine(target.FullName, file.Name));
    }

    private void CleanUp()
    {
        Log("正在清理工作目录...", WorkDir);
        if (Directory.Exists(WorkDir))
        {
            Directory.Delete(WorkDir, true);
        }

        Log("工作目录已清理完成");
    }

    // Copied from GIMI-ModManager.WinUI/Services/UpdateChecker.cs
    private const string ReleasesApiUrl = "https://api.github.com/repos/Moonholder/JASM/releases?per_page=2";

    private async Task<ApiGitHubRelease?> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        Serilog.Log.Information("检查最新版本...");

        using var httpClient = CreateHttpClient();

        var result = await httpClient.GetAsync(ReleasesApiUrl, cancellationToken);
        if (!result.IsSuccessStatusCode)
        {
            return null;
        }

        var text = await result.Content.ReadAsStringAsync(cancellationToken);

        ApiGitHubRelease[]? gitHubReleases = null;
        try
        {
            gitHubReleases = JsonSerializer.Deserialize<ApiGitHubRelease[]>(text, AutoUpdaterGitHubJsonContext.Default.ApiGitHubReleaseArray);
        }
        catch (JsonException ex)
        {
            Serilog.Log.Warning(ex, "Failed to deserialize GitHub releases JSON");
            gitHubReleases = Array.Empty<ApiGitHubRelease>();
        }

        var latestReleases = (gitHubReleases ?? Array.Empty<ApiGitHubRelease>()).Where(r => !r.Prerelease);
        var latestVersion = latestReleases.OrderByDescending(r => new Version(r.TagName?.Trim('v') ?? ""));

        return latestVersion.FirstOrDefault();
    }

    private HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 2
        });
        httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        httpClient.DefaultRequestHeaders.Add("User-Agent", "JASM-Just_Another_Skin_Manager-Update-Checker");
        return httpClient;
    }



    private class GitHubRelease
    {
        public Version Version;
        public bool PreRelease;
        public DateTime PublishedAt = DateTime.MinValue;

        public Uri BrowserUrl = null!;
        public Uri DownloadUrl = null!;
        public string FileName = null!;
        public GitHubRelease() { }
    }

    private void Log(string logMessage, string? footer = null)
    {
        Serilog.Log.Information("Install Step {StepIndex} | Msg: {Message} | footer: {Footer}", (ProgressLog.Count + 1),
            logMessage, footer);

        var logEntry = new LogEntry
        {
            Message = logMessage,
            Footer = footer,
            TimeStamp = DateTime.Now
        };
        ProgressLog.Insert(0, logEntry);
    }

    [RelayCommand]
    private async Task StartJasm()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _newJasmExePath,
            UseShellExecute = true
        });
        await Task.Delay(500);
        Application.Current.Exit();
    }


    private async Task<ContentDialogResult> ShowDeleteWarning(ICollection<string> warningFiles)
    {
        var content = new ContentDialog
        {
            Title = "警告",
            PrimaryButtonText = "继续",
            DefaultButton = ContentDialogButton.Primary,
            SecondaryButtonText = "取消",
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var stackPanel = new StackPanel();

        stackPanel.Children.Add(new TextBlock
        {
            Text =
                "安装的JASM文件夹下的所有文件/文件夹将被永久删除!\n" +
                "这不包括更新文件夹本身。此操作不能撤消.\n" +
                $"JASM 文件夹路径: {_installedJasmFolder.FullName}",
            TextWrapping = TextWrapping.WrapWholeWords,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(0, 0, 0, 10)
        });

        if (warningFiles.Any())
            stackPanel.Children.Add(new TextBlock
            {
                Text = "这些文件/文件夹不属于JASM，也将被删除:\n" +
                       string.Join("\n", warningFiles),
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.WrapWholeWords,
                Margin = new Thickness(0, 0, 0, 10)
            });

        stackPanel.Children.Add(new Button()
        {
            Content = "打开已安装的JASM文件夹...",
            Margin = new Thickness(0, 8, 0, 8),
            Command = new AsyncRelayCommand(async () =>
            {
                await Launcher.LaunchFolderAsync(
                    await StorageFolder.GetFolderFromPathAsync(_installedJasmFolder.FullName));
            })
        });

        content.Content = stackPanel;

        return await content.ShowAsync();
    }
}

public class ApiAssets
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}

public class LogEntry
{
    public string Message { get; set; } = string.Empty;
    public string? Footer { get; set; }
    public DateTime TimeStamp { get; set; } = DateTime.Now;
}

public partial class UpdateProgress : ObservableObject
{
    [ObservableProperty] private bool _checkingForLatestUpdate = false;

    [ObservableProperty] private bool _downloadingLatestUpdate = false;

    [ObservableProperty] private bool _extractingLatestUpdate = false;

    [ObservableProperty] private bool _installingLatestUpdate = false;

    public void Reset()
    {
        CheckingForLatestUpdate = false;
        DownloadingLatestUpdate = false;
        ExtractingLatestUpdate = false;
        InstallingLatestUpdate = false;
    }

    public void NextStage()
    {
        if (!CheckingForLatestUpdate)
        {
            CheckingForLatestUpdate = true;
        }
        else if (!DownloadingLatestUpdate)
        {
            DownloadingLatestUpdate = true;
        }
        else if (!ExtractingLatestUpdate)
        {
            ExtractingLatestUpdate = true;
        }
        else if (!InstallingLatestUpdate)
        {
            InstallingLatestUpdate = true;
        }
    }
}