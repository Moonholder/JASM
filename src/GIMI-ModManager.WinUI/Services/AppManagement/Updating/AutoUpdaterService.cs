using ErrorOr;
using GIMI_ModManager.Core.Contracts.Services;
using Microsoft.UI.Xaml;
using Serilog;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using static GIMI_ModManager.WinUI.Services.AppManagement.Updating.UpdateDownloadProgressEventArgs;

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

            for (int i = 0; i < availableMirrors.Count; i++)
            {
                var mirror = availableMirrors[i];
                bool isLastMirror = (i == availableMirrors.Count - 1);

                if (cancellationToken.IsCancellationRequested) break;
                var downloadUrl = mirror.Address + setupAsset.BrowserDownloadUrl!;

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
    /// Downloads a file with retry + resume support for a single mirror.
    /// Each mirror attempt starts from 0; retries within the same mirror use Range headers.
    /// </summary>
    private async Task DownloadFileAsync(string url, string destinationPath, long expectedSize,
    string mirrorName, bool isLastMirror, CancellationToken cancellationToken)
    {
        using var httpClient = CreateDownloadHttpClient();
        long totalBytes = expectedSize;
        bool supportsRange = false;

        // 1. Send a test request to detect if the server supports chunk download (Range)
        try
        {
            using var testReq = new HttpRequestMessage(HttpMethod.Get, url);
            testReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var testRes = await httpClient.SendAsync(testReq, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (testRes.StatusCode == System.Net.HttpStatusCode.PartialContent)
            {
                supportsRange = true;
                if (testRes.Content.Headers.ContentRange?.Length != null)
                    totalBytes = testRes.Content.Headers.ContentRange.Length.Value;
            }
            else if (testRes.IsSuccessStatusCode)
            {
                totalBytes = testRes.Content.Headers.ContentLength ?? expectedSize;
            }
        }
        catch { /* Ignore and use single thread */ }

        // 2. Determine the number of threads (4 threads if greater than 5MB and supports chunking)
        int threads = (supportsRange && totalBytes > 5 * 1024 * 1024) ? 4 : 1;
        var state = new DownloadState(threads);

        // 3. Pre-create file and allocate size (prevent disk fragmentation during multi-threaded writing)
        if (File.Exists(destinationPath)) File.Delete(destinationPath); // Delete each time the source is changed, discard cross-source fragments
        using (var fs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            if (threads > 1 && totalBytes > 0) fs.SetLength(totalBytes);
        }

        using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool isSlowTimeout = false;

        // 4. Independent task: global speed and progress monitoring
        var monitorTask = Task.Run(async () =>
        {
            int slowSpeedSeconds = 0;
            var sw = Stopwatch.StartNew();

            while (!monitorCts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, monitorCts.Token);
                if (monitorCts.Token.IsCancellationRequested) break;

                // Get the download amount in the past 1 second and reset it
                long bytesThisSecond = Interlocked.Exchange(ref state.SpeedBytes, 0);
                double currentSpeed = bytesThisSecond / (sw.ElapsedMilliseconds / 1000.0);
                sw.Restart();

                long received = state.GetTotalReceived();

                // Slow speed escape mechanism: non-last source, total speed is less than 50 KB/s for 10 seconds
                if (!isLastMirror && currentSpeed < 50 * 1024)
                {
                    slowSpeedSeconds++;
                    if (slowSpeedSeconds >= 10)
                    {
                        isSlowTimeout = true;
                        monitorCts.Cancel();
                        break;
                    }
                }
                else
                {
                    slowSpeedSeconds = 0;
                }

                int progress = totalBytes > 0 ? (int)(received * 100 / totalBytes) : 0;
                DownloadProgressChanged?.Invoke(this, new UpdateDownloadProgressEventArgs(
                    progress, received, totalBytes, mirrorName, currentSpeed));
            }
        });

        // 5. Start multi-threaded chunk download
        try
        {
            long chunkSize = totalBytes / threads;
            var downloadTasks = new List<Task>();

            for (int i = 0; i < threads; i++)
            {
                long start = i * chunkSize;
                long? end = (i == threads - 1) ? totalBytes - 1 : start + chunkSize - 1;
                if (threads == 1) end = null; // Single thread without upper limit

                downloadTasks.Add(DownloadChunkAsync(httpClient, url, destinationPath, start, end, i, state, monitorCts.Token));
            }

            await Task.WhenAll(downloadTasks);
        }
        catch (OperationCanceledException)
        {
            if (isSlowTimeout)
            {
                _logger.Warning("Download from {MirrorName} is too slow. Forcing mirror switch...", mirrorName);
                throw new TimeoutException("Mirror download speed is too slow.");
            }
            throw;
        }
        finally
        {
            monitorCts.Cancel();
            try { await monitorTask; } catch { }
        }

        // 100% progress notification
        DownloadProgressChanged?.Invoke(this, new UpdateDownloadProgressEventArgs(
            100, totalBytes, totalBytes, mirrorName, 0));
    }

    private async Task DownloadChunkAsync(HttpClient httpClient, string url, string destinationPath,
    long start, long? end, int threadIndex, DownloadState state, CancellationToken ct)
    {
        const int maxRetries = 3;
        const int bufferSize = 65536;

        long initialStart = start;
        long currentStart = start;
        long chunkReceived = 0;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (end.HasValue)
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(currentStart, end.Value);
                else if (currentStart > 0)
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(currentStart, null);

                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                // If the server rejects the breakpoint resume (e.g., returns 200 instead of 206 during retry), reset the current chunk's progress
                bool isPartial = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                if (!isPartial && currentStart > initialStart)
                {
                    currentStart = initialStart;
                    chunkReceived = 0;
                }

                // FileShare.ReadWrite is the core, allowing multiple threads to write to different positions in the same file
                using var fs = new FileStream(destinationPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, bufferSize, true);
                fs.Position = currentStart;

                using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[bufferSize];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, bytesRead), ct);

                    chunkReceived += bytesRead;
                    currentStart += bytesRead; // Advance the pointer, if disconnected, the next retry will start from the new pointer

                    // Synchronize global state
                    Interlocked.Exchange(ref state.ChunkProgress[threadIndex], chunkReceived);
                    Interlocked.Add(ref state.SpeedBytes, bytesRead);
                }

                return; // Successfully downloaded the chunk
            }
            catch (OperationCanceledException)
            {
                throw; // Monitor thread cancelled or user cancelled, throw directly
            }
            catch (Exception ex) when (retry < maxRetries - 1)
            {
                _logger.Warning(ex, "Chunk {ThreadIndex} interrupted at offset {CurrentStart}. Retrying {Retry}/{Max}...",
                    threadIndex, currentStart, retry + 1, maxRetries);
                await Task.Delay(1500, ct); // Wait a moment and then resume downloading the current chunk
            }
        }

        throw new IOException($"Thread {threadIndex} chunk download failed after {maxRetries} retries.");
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

    public class DownloadState
    {
        public long[] ChunkProgress;
        public long SpeedBytes;

        public DownloadState(int threads)
        {
            ChunkProgress = new long[threads];
        }

        public long GetTotalReceived() => ChunkProgress.Sum();
    }
}