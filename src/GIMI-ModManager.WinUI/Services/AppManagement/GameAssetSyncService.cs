using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GIMI_ModManager.WinUI.Services.AppManagement.Updating;
using Serilog;
using System.IO.Hashing;

namespace GIMI_ModManager.WinUI.Services.AppManagement;

/// <summary>
/// Manages incremental synchronization of game assets from a remote GitHub repository.
/// Uses a manifest.json to compare local vs remote files, downloads only changed/new files,
/// and cleans up orphaned local files.
/// </summary>
public class GameAssetSyncService
{
    private readonly ILogger _logger;

    // Shared HttpClient for connection reuse (TCP + TLS handshake amortization)
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        MaxConnectionsPerServer = 16,
        EnableMultipleHttp2Connections = true
    };

    private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

    private const string RepoOwner = "Moonholder";
    private const string RepoName = "JASM-GameAssets";

    // manifest.json MUST NOT go through jsDelivr (aggressive 24h+ cache).
    // Use GitHub Raw + mirror fallback to always get the freshest manifest.
    private const string ManifestRawUrl =
        $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/manifest.json";

    // Static assets CAN go through jsDelivr (fast CDN with China nodes).
    // Hash mismatch triggers automatic fallback to Raw.
    private const string JsDelivrBaseUrl =
        $"https://cdn.jsdelivr.net/gh/{RepoOwner}/{RepoName}@main/";

    private const string RawBaseUrl =
        $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/";

    private const int MaxParallelDownloads = 8;
    private const int MaxRetries = 3;

    private static readonly string LocalAssetRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JASM", "GameAssets");

    private static readonly string LocalManifestPath = Path.Combine(LocalAssetRoot, "manifest.json");

    // Bundled assets in the install directory — used as copy source to avoid redundant downloads
    private static readonly string BundledAssetRoot = Path.Combine(
        AppContext.BaseDirectory, "Assets", "Games");

    private readonly SemaphoreSlim _syncLock = new(1, 1);

    /// <summary>
    /// Raised to report sync progress. Consumers MUST marshal to UI thread via DispatcherQueue.
    /// </summary>
    public event EventHandler<AssetSyncProgressEventArgs>? SyncProgressChanged;

    public GameAssetSyncService(ILogger logger)
    {
        _logger = logger.ForContext<GameAssetSyncService>();
    }

    /// <summary>
    /// Gets the local game assets directory for a given game,
    /// with fallback to the bundled assets in the install directory.
    /// </summary>
    public static string GetGameAssetsDirectory(string gameName)
    {
        var remoteDir = Path.Combine(LocalAssetRoot, gameName);
        if (Directory.Exists(remoteDir) && File.Exists(Path.Combine(remoteDir, "game.json")))
            return remoteDir;
        // Fallback to bundled assets in install directory
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Games", gameName);
    }

    /// <summary>
    /// Checks if a newer version of game assets is available remotely.
    /// Returns the remote manifest version string if an update is available, null otherwise.
    /// </summary>
    public async Task<string?> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        try
        {
            var remoteManifest = await DownloadManifestAsync(ct).ConfigureAwait(false);
            if (remoteManifest is null) return null;

            var localManifest = await ReadLocalManifestAsync().ConfigureAwait(false);
            if (localManifest is null) return remoteManifest.Version;

            if (localManifest.Version != remoteManifest.Version)
                return remoteManifest.Version;

            // Same version string, but check if files actually differ
            var diff = ComputeDiff(localManifest, remoteManifest);
            return diff.HasChanges ? remoteManifest.Version : null;
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to check for game asset updates.");
            return null;
        }
    }

    /// <summary>
    /// Performs a full incremental sync: download changed files, delete orphans, update local manifest.
    /// </summary>
    public async Task<AssetSyncResult> SyncAsync(CancellationToken ct = default)
    {
        if (!await _syncLock.WaitAsync(0, ct).ConfigureAwait(false))
        {
            _logger.Warning("Sync already in progress.");
            return new AssetSyncResult(false, "Sync already in progress.");
        }

        try
        {
            _logger.Information("Starting game asset sync...");
            RaiseProgress(AssetSyncState.CheckingForUpdates, 0);

            // 1. Download remote manifest (never via jsDelivr)
            var remoteManifest = await DownloadManifestAsync(ct).ConfigureAwait(false);
            if (remoteManifest is null)
                return new AssetSyncResult(false, "Failed to download remote manifest.");

            // 2. Read local manifest
            var localManifest = await ReadLocalManifestAsync().ConfigureAwait(false);

            // 3. Compute diff
            var diff = ComputeDiff(localManifest, remoteManifest);
            if (!diff.HasChanges)
            {
                _logger.Information("Game assets are up to date (version: {Version}).", remoteManifest.Version);
                // Ensure local manifest is written even if no file changes
                await WriteLocalManifestAsync(remoteManifest).ConfigureAwait(false);
                RaiseProgress(AssetSyncState.Completed, 100);
                return new AssetSyncResult(true, "Already up to date.", 0, 0);
            }

            _logger.Information("Found {Download} files to download, {Delete} orphan files to remove.",
                diff.FilesToDownload.Count, diff.FilesToDelete.Count);

            // 4. Get available mirrors for fallback
            var mirrors = await MirrorAddressSelector.GetAvailableMirrorsAsync(ct).ConfigureAwait(false);

            // Calculate total download size for logging
            var totalBytes = diff.FilesToDownload.Sum(f => f.Size);
            _logger.Information("Total download size: {SizeMB:F2} MB", totalBytes / 1024.0 / 1024.0);

            // 5. Download changed/new files with parallel + atomic writes
            RaiseProgress(AssetSyncState.Downloading, 0);
            var downloadedCount = 0;
            var totalToDownload = diff.FilesToDownload.Count;
            var failedFiles = new ConcurrentBag<string>();

            await Parallel.ForEachAsync(
                diff.FilesToDownload,
                new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads, CancellationToken = ct },
                async (fileEntry, token) =>
                {
                    var success = await DownloadFileWithFallbackAsync(fileEntry, mirrors, token)
                        .ConfigureAwait(false);

                    if (!success)
                        failedFiles.Add(fileEntry.Path);

                    var current = Interlocked.Increment(ref downloadedCount);
                    var progress = totalToDownload > 0 ? (int)(current * 100 / totalToDownload) : 100;
                    RaiseProgress(AssetSyncState.Downloading, progress, fileEntry.Path);
                }).ConfigureAwait(false);

            if (!failedFiles.IsEmpty)
            {
                _logger.Error("Failed to download {Count} files: {Files}",
                    failedFiles.Count, string.Join(", ", failedFiles));
            }

            // 6. Delete orphaned files
            RaiseProgress(AssetSyncState.CleaningUp, 90);
            var deletedCount = 0;
            foreach (var orphanPath in diff.FilesToDelete)
            {
                try
                {
                    var fullPath = Path.Combine(LocalAssetRoot, orphanPath);
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        deletedCount++;
                        _logger.Debug("Deleted orphan file: {Path}", orphanPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to delete orphan file: {Path}", orphanPath);
                }
            }

            CleanupEmptyDirectories(LocalAssetRoot);

            // 7. Write updated local manifest
            await WriteLocalManifestAsync(remoteManifest).ConfigureAwait(false);

            _logger.Information(
                "Game asset sync completed. Downloaded: {Downloaded}, Deleted: {Deleted}, Failed: {Failed}",
                downloadedCount - failedFiles.Count, deletedCount, failedFiles.Count);

            RaiseProgress(AssetSyncState.Completed, 100);

            return new AssetSyncResult(
                failedFiles.IsEmpty,
                failedFiles.IsEmpty ? "Sync completed successfully." : $"Sync completed with {failedFiles.Count} failures.",
                downloadedCount - failedFiles.Count,
                deletedCount);
        }
        catch (OperationCanceledException)
        {
            _logger.Information("Game asset sync was cancelled.");
            RaiseProgress(AssetSyncState.Cancelled, 0);
            return new AssetSyncResult(false, "Sync was cancelled.", IsCancelled: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Game asset sync failed.");
            RaiseProgress(AssetSyncState.Failed, 0);
            return new AssetSyncResult(false, $"Sync failed: {ex.Message}");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    #region Manifest Operations

    private async Task<AssetManifest?> DownloadManifestAsync(CancellationToken ct)
    {
        // manifest.json must NOT go through jsDelivr (aggressive caching).
        // Try mirrors first, then direct GitHub Raw.
        var mirrors = await MirrorAddressSelector.GetAvailableMirrorsAsync(ct).ConfigureAwait(false);

        foreach (var mirror in mirrors)
        {
            try
            {
                var url = mirror.Address + ManifestRawUrl;
                _logger.Debug("Downloading manifest from {NodeName}: {Url}", mirror.NodeName, url);

                var json = await SharedHttpClient.GetStringAsync(url, ct).ConfigureAwait(false);
                var manifest = JsonSerializer.Deserialize(json, AssetManifestJsonContext.Default.AssetManifest);

                if (manifest?.Files is { Count: > 0 })
                {
                    _logger.Information("Downloaded manifest v{Version} with {Count} files from {NodeName}.",
                        manifest.Version, manifest.Files.Count, mirror.NodeName);
                    return manifest;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to download manifest from {NodeName}.", mirror.NodeName);
            }
        }

        _logger.Error("All mirrors failed to download manifest.");
        return null;
    }

    private static async Task<AssetManifest?> ReadLocalManifestAsync()
    {
        if (!File.Exists(LocalManifestPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(LocalManifestPath).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, AssetManifestJsonContext.Default.AssetManifest);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteLocalManifestAsync(AssetManifest manifest)
    {
        Directory.CreateDirectory(LocalAssetRoot);
        var json = JsonSerializer.Serialize(manifest, AssetManifestJsonContext.Default.AssetManifest);
        await File.WriteAllTextAsync(LocalManifestPath, json).ConfigureAwait(false);
    }

    #endregion

    #region Diff Computation

    private static ManifestDiff ComputeDiff(AssetManifest? local, AssetManifest remote)
    {
        var localFiles = new Dictionary<string, AssetFileEntry>(StringComparer.OrdinalIgnoreCase);
        if (local?.Files != null)
        {
            foreach (var f in local.Files)
                localFiles[f.Path] = f;
        }

        var filesToDownload = new List<AssetFileEntry>();
        var remoteFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var remoteFile in remote.Files)
        {
            remoteFilePaths.Add(remoteFile.Path);

            if (!localFiles.TryGetValue(remoteFile.Path, out var localFile))
            {
                // New file
                filesToDownload.Add(remoteFile);
            }
            else if (!string.Equals(localFile.Hash, remoteFile.Hash, StringComparison.OrdinalIgnoreCase))
            {
                // Changed file
                filesToDownload.Add(remoteFile);
            }
            else
            {
                // Same hash in manifest, but verify the local file actually exists
                var localPath = Path.Combine(LocalAssetRoot, remoteFile.Path);
                if (!File.Exists(localPath))
                    filesToDownload.Add(remoteFile);
            }
        }

        // Files in local but not in remote = orphans to delete
        var filesToDelete = localFiles.Keys
            .Where(path => !remoteFilePaths.Contains(path))
            .ToList();

        return new ManifestDiff(filesToDownload, filesToDelete);
    }

    #endregion

    #region File Download

    /// <summary>
    /// Downloads a single file with Bundled → CDN → Mirror fallback and atomic write.
    /// On first sync, most files match the bundled copy and are simply copied locally.
    /// </summary>
    private async Task<bool> DownloadFileWithFallbackAsync(
        AssetFileEntry fileEntry,
        List<MirrorAddressSelector.MirrorInfo> mirrors,
        CancellationToken ct)
    {
        var localPath = Path.Combine(LocalAssetRoot, fileEntry.Path);
        var tmpPath = localPath + ".tmp";

        // Ensure target directory exists
        var dir = Path.GetDirectoryName(localPath);
        if (dir != null) Directory.CreateDirectory(dir);

        // Strategy 0: Copy from bundled install directory if hash matches
        // This avoids redundant network downloads on first sync, since bundled assets
        // are identical to the remote repo at build time.
        try
        {
            if (await TryCopyFromBundledAsync(fileEntry, localPath, ct).ConfigureAwait(false))
                return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Bundled copy failed for {Path}, falling back to network.", fileEntry.Path);
        }

        // Strategy 1: jsDelivr CDN (fast, but may serve cached old version)
        try
        {
            var cdnUrl = JsDelivrBaseUrl + fileEntry.Path;
            if (await DownloadAndVerifyAsync(cdnUrl, tmpPath, localPath, fileEntry.Hash, ct)
                    .ConfigureAwait(false))
                return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.Debug(ex, "jsDelivr failed for {Path}, trying mirrors...", fileEntry.Path);
        }

        // Strategy 2: GitHub mirrors
        foreach (var mirror in mirrors)
        {
            try
            {
                var url = mirror.Address + RawBaseUrl + fileEntry.Path;
                if (await DownloadAndVerifyAsync(url, tmpPath, localPath, fileEntry.Hash, ct)
                        .ConfigureAwait(false))
                    return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Mirror {NodeName} failed for {Path}.", mirror.NodeName, fileEntry.Path);
            }
        }

        _logger.Error("All download sources failed for {Path}", fileEntry.Path);
        CleanupTmpFile(tmpPath);
        return false;
    }

    /// <summary>
    /// Checks if the file exists in the bundled install directory with matching XxHash3.
    /// If so, copies it to the AppData sync directory instead of downloading.
    /// </summary>
    private async Task<bool> TryCopyFromBundledAsync(
        AssetFileEntry fileEntry, string targetPath, CancellationToken ct)
    {
        var bundledPath = Path.Combine(BundledAssetRoot, fileEntry.Path);
        if (!File.Exists(bundledPath))
            return false;

        var bundledHash = await ComputeFileHashAsync(bundledPath, ct).ConfigureAwait(false);
        if (!string.Equals(bundledHash, fileEntry.Hash, StringComparison.OrdinalIgnoreCase))
            return false;

        // Hash matches — copy instead of download
        File.Copy(bundledPath, targetPath, overwrite: true);
        _logger.Debug("Copied from bundled: {Path}", fileEntry.Path);
        return true;
    }

    /// <summary>
    /// Downloads a file to tmp, verifies XxHash3, then atomically moves to final destination.
    /// Returns true if download + verification succeeded.
    /// </summary>
    private async Task<bool> DownloadAndVerifyAsync(
        string url, string tmpPath, string finalPath, string expectedHash, CancellationToken ct)
    {
        for (var retry = 0; retry < MaxRetries; retry++)
        {
            try
            {
                using var response = await SharedHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                {
                    await response.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
                }

                // Verify hash
                var actualHash = await ComputeFileHashAsync(tmpPath, ct).ConfigureAwait(false);
                if (string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    // Atomic move: tmp → final
                    File.Move(tmpPath, finalPath, overwrite: true);
                    return true;
                }

                _logger.Warning("Hash mismatch for {Url}: expected {Expected}, got {Actual}. Retry {Retry}/{Max}",
                    url, expectedHash, actualHash, retry + 1, MaxRetries);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (retry < MaxRetries - 1)
            {
                _logger.Debug(ex, "Download attempt {Retry}/{Max} failed for {Url}", retry + 1, MaxRetries, url);
                await Task.Delay(500 * (retry + 1), ct).ConfigureAwait(false);
            }
        }

        CleanupTmpFile(tmpPath);
        return false;
    }

    private static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken ct)
    {
        await using var fs = File.OpenRead(filePath);
        var hasher = new XxHash3();
        await hasher.AppendAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hasher.GetCurrentHash());
    }

    #endregion

    #region Helpers

    private static HttpClient CreateSharedHttpClient()
    {
        var httpClient = new HttpClient(SharedHandler, disposeHandler: false);
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "JASM-GameAssetSync");
        return httpClient;
    }

    private void RaiseProgress(AssetSyncState state, int progressPercent, string? currentFile = null)
    {
        SyncProgressChanged?.Invoke(this, new AssetSyncProgressEventArgs(state, progressPercent, currentFile));
    }

    private static void CleanupTmpFile(string tmpPath)
    {
        try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Recursively removes empty directories under the given root.
    /// </summary>
    private static void CleanupEmptyDirectories(string rootPath)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length)) // deepest first
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
        }
        catch { /* best effort */ }
    }

    #endregion
}

#region Models

public class AssetManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("files")]
    public List<AssetFileEntry> Files { get; set; } = [];
}

public class AssetFileEntry
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public record ManifestDiff(List<AssetFileEntry> FilesToDownload, List<string> FilesToDelete)
{
    public bool HasChanges => FilesToDownload.Count > 0 || FilesToDelete.Count > 0;
}

public record AssetSyncResult(bool Success, string Message, int DownloadedCount = 0, int DeletedCount = 0, bool IsCancelled = false);

public enum AssetSyncState
{
    CheckingForUpdates,
    Downloading,
    CleaningUp,
    Completed,
    Failed,
    Cancelled
}

public class AssetSyncProgressEventArgs : EventArgs
{
    public AssetSyncState State { get; }
    public int ProgressPercent { get; }
    public string? CurrentFile { get; }

    public AssetSyncProgressEventArgs(AssetSyncState state, int progressPercent, string? currentFile = null)
    {
        State = state;
        ProgressPercent = progressPercent;
        CurrentFile = currentFile;
    }
}

/// <summary>
/// Source-generated JSON context for AOT/trimming compatibility.
/// </summary>
[JsonSerializable(typeof(AssetManifest))]
[JsonSerializable(typeof(List<AssetFileEntry>))]
internal partial class AssetManifestJsonContext : JsonSerializerContext;

#endregion