using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Services.Network;
using GIMI_ModManager.Core.Services.Network.Models;
using Serilog;

namespace GIMI_ModManager.WinUI.Services.AppManagement.Updating;

public class AutoUpdaterService
{
    private readonly ILogger _logger;
    private readonly UpdateChecker _updateChecker;
    private readonly ILanguageLocalizer _localizer;
    private readonly DynamicAppWebProxy _dynamicProxy;
    private readonly IMirrorRacingService _mirrorRacingService;
    private readonly IChunkedDownloadEngine _chunkedDownloadEngine;

    private const string ReleasesApiUrl = "https://api.github.com/repos/Moonholder/JASM/releases?per_page=2";
    private const string SetupFilePrefix = "JASM_v";
    private const string SetupFileSuffix = "_Setup.exe";

    private static bool HasStartedSelfUpdateProcess { get; set; }

    /// <summary>
    /// Event raised to report download progress.
    /// </summary>
    public event EventHandler<UpdateDownloadProgressEventArgs>? DownloadProgressChanged;

    public AutoUpdaterService(
        ILogger logger,
        UpdateChecker updateChecker,
        ILanguageLocalizer localizer,
        DynamicAppWebProxy dynamicProxy,
        IMirrorRacingService mirrorRacingService,
        IChunkedDownloadEngine chunkedDownloadEngine)
    {
        _updateChecker = updateChecker;
        _localizer = localizer;
        _logger = logger.ForContext<AutoUpdaterService>();
        _dynamicProxy = dynamicProxy;
        _mirrorRacingService = mirrorRacingService;
        _chunkedDownloadEngine = chunkedDownloadEngine;

        // Clean up leftover update files from previous session
        _ = Task.Run(() =>
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "JASM_Update");
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to clean old JASM_Update directory on startup.");
            }
        });
    }

    /// <summary>
    /// Downloads the latest Setup.exe from GitHub Release and launches it in silent mode.
    /// Supports multi-mirror failover with per-mirror retry and resume.
    /// </summary>
    public async Task<Error[]?> DownloadAndInstallUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (HasStartedSelfUpdateProcess)
        {
            _logger.Warning("Self update process already started.");
            return [Error.Conflict(description: _localizer.GetLocalizedStringOrDefault(
                "/Settings/AutoUpdater_ProcessAlreadyStarted", "Self update process already started."))];
        }

        HasStartedSelfUpdateProcess = true;
        try
        {
            // 1. Fetch latest release info
            _logger.Information("Fetching latest release info from GitHub...");
            var release = await GetLatestReleaseAsync(cancellationToken);
            if (release is null)
            {
                return [Error.NotFound(description: _localizer.GetLocalizedStringOrDefault(
                    "/Settings/AutoUpdater_NoReleaseFound", "Could not find latest release on GitHub."))];
            }

            // 2. Find the Setup.exe asset
            var setupAsset = FindSetupAsset(release);
            if (setupAsset is null)
            {
                _logger.Warning("No Setup.exe found in release assets.");
                return [Error.NotFound(description: _localizer.GetLocalizedStringOrDefault(
                    "/Settings/AutoUpdater_ExeNotFound", "Could not find the Setup executable in release assets."))];
            }

            // 3. Prepare temp download directory
            var tempDir = Path.Combine(Path.GetTempPath(), "JASM_Update");
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception e) { _logger.Warning(e, "Failed to clean old JASM_Update directory before download."); }
            }
            Directory.CreateDirectory(tempDir);
            var setupPath = Path.Combine(tempDir, setupAsset.Name!);

            // 4. Test mirrors and download with failover
            _logger.Information("Testing mirrors for faster download...");
            var availableMirrors = await _mirrorRacingService.GetRankedMirrorsAsync(setupAsset.BrowserDownloadUrl!, cancellationToken: cancellationToken);
            var success = false;

            for (int i = 0; i < availableMirrors.Count; i++)
            {
                var mirror = availableMirrors[i];
                bool isLastMirror = (i == availableMirrors.Count - 1);

                if (cancellationToken.IsCancellationRequested) break;
                var downloadUrl = string.IsNullOrEmpty(mirror.Address)
                    ? setupAsset.BrowserDownloadUrl!
                    : mirror.Address.TrimEnd('/') + "/" + setupAsset.BrowserDownloadUrl!;

                try
                {
                    _logger.Information("Downloading from {NodeName}...", mirror.NodeName);

                    // Notify UI which mirror is being used
                    DownloadProgressChanged?.Invoke(this, new UpdateDownloadProgressEventArgs(0, 0, setupAsset.Size, mirror.NodeName));

                    await DownloadFileAsync(downloadUrl, setupPath, setupAsset.Size, mirror.NodeName, isLastMirror, cancellationToken);
                    success = true;
                    _logger.Information("Download completed via {NodeName}.", mirror.NodeName);
                    break;
                }
                catch (OperationCanceledException)
                {
                    throw; // User cancelled, don't try next mirror
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed from {NodeName}. Trying next mirror...", mirror.NodeName);
                }
            }

            if (!success)
            {
                return [Error.Failure(description: _localizer.GetLocalizedStringOrDefault(
                    "/Settings/AutoUpdater_DownloadFailed", "All mirrors failed to download the update package."))];
            }

            // 5. Launch Setup directly
            var installDir = App.ROOT_DIR.TrimEnd(Path.DirectorySeparatorChar);

            _logger.Information("Launching Setup directly: {SetupPath}", setupPath);

            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = setupPath,
                    Arguments = $"/SILENT /SP- /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /DIR=\"{installDir}\" /AUTORUN",
                    UseShellExecute = true
                });

                if (process is null)
                {
                    _logger.Error("Process.Start returned null for Setup.");
                    return [Error.Unexpected(description: _localizer.GetLocalizedStringOrDefault(
                        "/Settings/AutoUpdater_StartFailed", "Failed to start Auto Updater."))];
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception while starting setup.");
                return [Error.Unexpected(description: _localizer.GetLocalizedStringOrDefault(
                    "/Settings/AutoUpdater_StartFailed", "Failed to start Auto Updater."))];
            }

            _logger.Information("Setup started. Exiting application for update...");

            Environment.Exit(0);
            return null;
        }
        catch (OperationCanceledException)
        {
            _logger.Information("Update download was cancelled.");
            return [Error.Failure(description: _localizer.GetLocalizedStringOrDefault(
                "/Settings/AutoUpdater_Cancelled", "Update was cancelled."))];
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to download and install update.");
            return [Error.Unexpected(description: string.Format(
                _localizer.GetLocalizedStringOrDefault(
                    "/Settings/AutoUpdater_Exception", "An error occurred while downloading the update. Error: {0}"),
                e.Message))];
        }
        finally
        {
            HasStartedSelfUpdateProcess = false;
        }
    }

    private async Task<GitHubRelease?> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var httpClient = CreateHttpClient();
        try
        {
            var result = await httpClient.GetAsync(ReleasesApiUrl, cancellationToken);
            if (!result.IsSuccessStatusCode)
            {
                _logger.Error("Failed to fetch releases from GitHub. Status: {StatusCode}", result.StatusCode);
                return null;
            }

            var text = await result.Content.ReadAsStringAsync(cancellationToken);
            var releases = JsonSerializer.Deserialize<GitHubRelease[]>(text, GitHubJsonContext.Default.GitHubReleaseArray)
                           ?? Array.Empty<GitHubRelease>();

            return releases
                .Where(r => !r.Prerelease)
                .OrderByDescending(r => new Version(r.TagName?.Trim('v') ?? "0.0.0"))
                .FirstOrDefault();
        }
        catch (HttpRequestException e)
        {
            _logger.Warning(e, "SSL/Connection error fetching releases.");
            return null;
        }
        catch (Exception e)
        {
            _logger.Error(e, "Fetch releases unknown error.");
            return null;
        }
    }

    private static GitHubReleaseAsset? FindSetupAsset(GitHubRelease release)
    {
        return release.Assets?.FirstOrDefault(a =>
            a.Name != null &&
            a.Name.StartsWith(SetupFilePrefix, StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(SetupFileSuffix, StringComparison.OrdinalIgnoreCase) &&
            a.BrowserDownloadUrl != null);
    }

    /// <summary>
    /// Downloads the update package using the universal chunked download engine.
    /// </summary>
    private async Task DownloadFileAsync(
        string url,
        string destinationPath,
        long expectedSize,
        string mirrorName,
        bool isLastMirror,
        CancellationToken cancellationToken)
    {
        var progress = new Progress<DownloadProgressInfo>(info =>
        {
            DownloadProgressChanged?.Invoke(this, new UpdateDownloadProgressEventArgs(
                info.Percentage ?? 0,
                info.BytesReceived,
                info.TotalBytes ?? expectedSize,
                mirrorName,
                info.BytesPerSecond));
        });

        var options = new ChunkedDownloadOptions
        {
            Concurrency = 4,
            MinFileSizeForChunking = 5 * 1024 * 1024
        };

        await _chunkedDownloadEngine.DownloadAsync(url, destinationPath, progress, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates an HttpClient for API calls (GitHub Release info).
    /// </summary>
    private HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            Proxy = _dynamicProxy,
            UseProxy = true
        });
        httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        httpClient.DefaultRequestHeaders.Add("User-Agent", "JASM-Just_Another_Skin_Manager-Update-Checker");
        return httpClient;
    }
}

public class UpdateDownloadProgressEventArgs : EventArgs
{
    public int ProgressPercent { get; }
    public long BytesReceived { get; }
    public long TotalBytes { get; }
    public string? MirrorName { get; }
    public double SpeedBytesPerSecond { get; }

    public UpdateDownloadProgressEventArgs(int progressPercent, long bytesReceived, long totalBytes,
        string? mirrorName = null, double speedBytesPerSecond = 0)
    {
        ProgressPercent = progressPercent;
        BytesReceived = bytesReceived;
        TotalBytes = totalBytes;
        MirrorName = mirrorName;
        SpeedBytesPerSecond = speedBytesPerSecond;
    }
}