using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using ErrorOr;
using GIMI_ModManager.Core.Contracts.Services;
using Microsoft.UI.Xaml;
using Serilog;

namespace GIMI_ModManager.WinUI.Services.AppManagement.Updating;

public class AutoUpdaterService
{
    private readonly ILogger _logger;
    private readonly UpdateChecker _updateChecker;
    private readonly ILanguageLocalizer _localizer;

    private const string ReleasesApiUrl = "https://api.github.com/repos/Moonholder/JASM/releases?per_page=2";
    private const string SetupFilePrefix = "JASM_v";
    private const string SetupFileSuffix = "_Setup.exe";

    private static bool HasStartedSelfUpdateProcess { get; set; }

    /// <summary>
    /// Event raised to report download progress.
    /// </summary>
    public event EventHandler<UpdateDownloadProgressEventArgs>? DownloadProgressChanged;

    public AutoUpdaterService(ILogger logger, UpdateChecker updateChecker, ILanguageLocalizer localizer)
    {
        _updateChecker = updateChecker;
        _localizer = localizer;
        _logger = logger.ForContext<AutoUpdaterService>();

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
            var availableMirrors = await MirrorAddressSelector.GetAvailableMirrorsAsync(cancellationToken);
            var success = false;

            foreach (var mirror in availableMirrors)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var downloadUrl = mirror.Address + setupAsset.BrowserDownloadUrl!;

                try
                {
                    _logger.Information("Downloading from {NodeName}...", mirror.NodeName);

                    // Notify UI which mirror is being used
                    DownloadProgressChanged?.Invoke(this, new UpdateDownloadProgressEventArgs(0, 0, setupAsset.Size, mirror.NodeName));

                    await DownloadFileAsync(downloadUrl, setupPath, setupAsset.Size, mirror.NodeName, cancellationToken);
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

            // 5. Create .cmd wrapper script: wait → install → restart JASM
            var installDir = App.ROOT_DIR.TrimEnd(Path.DirectorySeparatorChar);
            var appExePath = Path.Combine(installDir, "JASM - Just Another Skin Manager.exe");
            var batchPath = Path.Combine(tempDir, "update.cmd");

            var batchContent = $"""
                @echo off
                timeout /t 2 /nobreak >nul
                "{setupPath}" /SILENT /CLOSEAPPLICATIONS /DIR="{installDir}"
                start "" "{appExePath}"
                del "%~f0"
                """;
            File.WriteAllText(batchPath, batchContent, System.Text.Encoding.Default);

            _logger.Information("Launching update script: {BatchPath}", batchPath);

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C \"{batchPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (process is null || process.HasExited)
            {
                _logger.Error("Failed to start update script.");
                return [Error.Unexpected(description: _localizer.GetLocalizedStringOrDefault(
                    "/Settings/AutoUpdater_StartFailed", "Failed to start Auto Updater."))];
            }

            _logger.Information("Update script started. Exiting application for update...");
            Application.Current.Exit();
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
    /// Downloads a file with retry + resume support for a single mirror.
    /// Each mirror attempt starts from 0; retries within the same mirror use Range headers.
    /// </summary>
    private async Task DownloadFileAsync(string url, string destinationPath, long expectedSize,
        string mirrorName, CancellationToken cancellationToken)
    {
        using var httpClient = CreateDownloadHttpClient();

        long totalBytes = expectedSize;
        long bytesReceived = 0;
        const int maxRetries = 3;
        const int bufferSize = 65536; // 64KB buffer for better throughput

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (bytesReceived > 0)
                {
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(bytesReceived, null);
                    _logger.Information("Resuming download from {Bytes} bytes (retry {Retry}/{Max})...",
                        bytesReceived, retry + 1, maxRetries);
                }

                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                // Determine if server supports Range and update totalBytes
                if (retry == 0 || totalBytes <= 0)
                    totalBytes = (response.Content.Headers.ContentLength ?? 0) + bytesReceived;

                // If server doesn't support Range (returns 200 instead of 206), restart from beginning
                var fileMode = bytesReceived > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent
                    ? FileMode.Append
                    : FileMode.Create;
                if (fileMode == FileMode.Create)
                    bytesReceived = 0;

                await using var fileStream = new FileStream(destinationPath, fileMode, FileAccess.Write, FileShare.None, bufferSize, true);
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);

                var buffer = new byte[bufferSize];
                int bytesRead;
                var speedTimer = Stopwatch.StartNew();
                long speedBytes = 0;
                double currentSpeed = 0;
                var lastProgressUpdate = Stopwatch.StartNew();

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    bytesReceived += bytesRead;
                    speedBytes += bytesRead;

                    // Update speed calculation every second
                    if (speedTimer.ElapsedMilliseconds >= 1000)
                    {
                        currentSpeed = speedBytes / (speedTimer.Elapsed.TotalSeconds);
                        speedTimer.Restart();
                        speedBytes = 0;
                    }

                    // Update UI progress every 500ms
                    if (lastProgressUpdate.ElapsedMilliseconds >= 500)
                    {
                        var progress = totalBytes > 0 ? (int)(bytesReceived * 100 / totalBytes) : 0;
                        DownloadProgressChanged?.Invoke(this, new UpdateDownloadProgressEventArgs(
                            progress, bytesReceived, totalBytes, mirrorName, currentSpeed));
                        lastProgressUpdate.Restart();
                    }
                }

                // Verify download completeness
                if (totalBytes > 0 && bytesReceived < totalBytes)
                    throw new IOException($"Connection closed prematurely. Received {bytesReceived} of {totalBytes} bytes.");

                // Final 100% progress
                DownloadProgressChanged?.Invoke(this, new UpdateDownloadProgressEventArgs(
                    100, bytesReceived, bytesReceived, mirrorName, currentSpeed));
                return;
            }
            catch (OperationCanceledException)
            {
                throw; // Don't retry on user cancellation
            }
            catch (Exception ex) when (retry < maxRetries - 1)
            {
                _logger.Warning(ex, "Download interrupted at {Bytes} bytes. Retrying {Retry}/{Max}...",
                    bytesReceived, retry + 1, maxRetries);
                await Task.Delay(1500, cancellationToken);
            }
        }

        throw new IOException($"Failed to download from {mirrorName} after {maxRetries} retries.");
    }

    /// <summary>
    /// Creates an HttpClient for API calls (GitHub Release info).
    /// </summary>
    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        });
        httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        httpClient.DefaultRequestHeaders.Add("User-Agent", "JASM-Just_Another_Skin_Manager-Update-Checker");
        return httpClient;
    }

    /// <summary>
    /// Creates an HttpClient for large file downloads with no timeout limit.
    /// </summary>
    private static HttpClient CreateDownloadHttpClient()
    {
        var httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10
        });
        httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan; // Large file, rely on CancellationToken
        httpClient.DefaultRequestHeaders.Add("User-Agent", "JASM-Just_Another_Skin_Manager-Update-Checker");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/octet-stream");
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