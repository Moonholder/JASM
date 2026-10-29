using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Services.Network.Models;
using GIMI_ModManager.Core.Services.Network.Serialization;
using Serilog;

namespace GIMI_ModManager.Core.Services.Network;

public sealed class ChunkedDownloadEngine : IChunkedDownloadEngine, IDisposable
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly object _metaLock = new();

    public ChunkedDownloadEngine(ILogger logger, DynamicAppWebProxy? dynamicProxy = null, HttpClient? httpClient = null)
    {
        _logger = logger?.ForContext<ChunkedDownloadEngine>() ?? throw new ArgumentNullException(nameof(logger));

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                MaxConnectionsPerServer = 32,
                EnableMultipleHttp2Connections = true
            };

            if (dynamicProxy != null)
            {
                handler.Proxy = dynamicProxy;
                handler.UseProxy = true;
            }

            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "JASM-Just_Another_Skin_Manager");
            _ownsHttpClient = true;
        }
    }

    public async Task<RangeProbeResult> ProbeRangeSupportAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 0);
            request.Headers.AcceptEncoding.Clear();
            if (url.Contains("gamebanana.com", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Referrer = new Uri("https://gamebanana.com/");
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var resolvedUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? url;

            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var totalLength = response.Content.Headers.ContentRange?.Length;
                return new RangeProbeResult(true, totalLength, response.StatusCode, resolvedUrl);
            }

            if (response.IsSuccessStatusCode)
            {
                var totalLength = response.Content.Headers.ContentLength;
                return new RangeProbeResult(false, totalLength, response.StatusCode, resolvedUrl);
            }

            return new RangeProbeResult(false, null, response.StatusCode, resolvedUrl);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to probe range capability for {Url}", url);
            return new RangeProbeResult(false, null, HttpStatusCode.ServiceUnavailable, url);
        }
    }

    public async Task DownloadAsync(
        string url,
        string destinationFilePath,
        IProgress<DownloadProgressInfo>? progress = null,
        ChunkedDownloadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        var downloadOptions = options ?? new ChunkedDownloadOptions();
        var destinationDir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        var probe = await ProbeRangeSupportAsync(url, cancellationToken).ConfigureAwait(false);
        var targetUrl = !string.IsNullOrWhiteSpace(probe.ResolvedUrl) ? probe.ResolvedUrl : url;

        if (probe.SupportsRange && probe.ContentLength.HasValue && probe.ContentLength.Value >= downloadOptions.MinFileSizeForChunking)
        {
            _logger.Information("Initiating multi-chunk parallel download for {Url} (Size: {Size} bytes, Concurrency: {Concurrency})",
                targetUrl, probe.ContentLength.Value, downloadOptions.Concurrency);
            await DownloadMultiChunkAsync(targetUrl, destinationFilePath, probe.ContentLength.Value, progress, downloadOptions, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.Information("Falling back to single-stream download for {Url} (RangeSupported: {RangeSupported}, ContentLength: {Length})",
                targetUrl, probe.SupportsRange, probe.ContentLength);
            await DownloadSingleStreamAsync(targetUrl, destinationFilePath, probe.ContentLength, progress, downloadOptions, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DownloadMultiChunkAsync(
        string url,
        string destinationFilePath,
        long totalBytes,
        IProgress<DownloadProgressInfo>? progress,
        ChunkedDownloadOptions options,
        CancellationToken cancellationToken)
    {
        var partPath = destinationFilePath + ".part";
        var metaPath = destinationFilePath + ".part.meta";

        var sessionMeta = LoadOrCreateSessionMeta(url, totalBytes, partPath, metaPath, options);

        using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        long speedCounter = 0;

        var monitorTask = RunProgressMonitorAsync(sessionMeta, totalBytes, progress, () => Interlocked.Exchange(ref speedCounter, 0), options, monitorCts.Token);

        try
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(options.Concurrency, 1, 16),
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(sessionMeta.Chunks, parallelOptions, async (chunk, token) =>
            {
                if (chunk.IsCompleted) return;

                await DownloadChunkWithRetryAsync(url, partPath, metaPath, sessionMeta, chunk, bytesRead =>
                {
                    Interlocked.Add(ref speedCounter, bytesRead);
                }, options, token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        finally
        {
            monitorCts.Cancel();
            try { await monitorTask.ConfigureAwait(false); } catch { /* Ignore monitor cancellation */ }

            lock (_metaLock)
            {
                if (sessionMeta.Chunks.Any(c => !c.IsCompleted))
                {
                    SaveMetaToFile_NoLock(metaPath, sessionMeta);
                }
            }
        }

        // Verify all chunks completed
        lock (_metaLock)
        {
            if (sessionMeta.Chunks.Any(c => !c.IsCompleted))
            {
                throw new IOException("Not all chunks completed successfully.");
            }
        }

        // Final completion notification
        progress?.Report(new DownloadProgressInfo(totalBytes, totalBytes, 0, 100, 0));

        // Clean up metadata file
        try
        {
            if (File.Exists(metaPath)) File.Delete(metaPath);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to clean up metadata file {MetaPath}", metaPath);
        }

        // Atomic file commit
        if (File.Exists(destinationFilePath))
        {
            File.Delete(destinationFilePath);
        }
        File.Move(partPath, destinationFilePath, overwrite: true);
        _logger.Information("Multi-chunk download completed successfully for {Destination}", destinationFilePath);
    }

    private DownloadSessionMetadata LoadOrCreateSessionMeta(
        string url,
        long totalBytes,
        string partPath,
        string metaPath,
        ChunkedDownloadOptions options)
    {
        lock (_metaLock)
        {
            if (File.Exists(metaPath) && File.Exists(partPath))
            {
                try
                {
                    var existingJson = File.ReadAllText(metaPath);
                    var existingMeta = JsonSerializer.Deserialize(existingJson, NetworkJsonContext.Default.DownloadSessionMetadata);
                    var fileInfo = new FileInfo(partPath);

                    if (existingMeta != null &&
                        existingMeta.Url == url &&
                        existingMeta.TotalBytes == totalBytes &&
                        fileInfo.Length == totalBytes &&
                        existingMeta.Chunks.Count > 0)
                    {
                        _logger.Information("Resuming existing multi-chunk download session from metadata ({Chunks} chunks)", existingMeta.Chunks.Count);
                        return existingMeta;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to resume metadata session, recreating part file.");
                }
            }

            // Fresh session initialization
            int chunkCount = Math.Clamp((int)(totalBytes / options.MinChunkSize), 1, options.Concurrency);
            long chunkSize = totalBytes / chunkCount;

            var chunks = new List<ChunkMetadata>(chunkCount);
            for (int i = 0; i < chunkCount; i++)
            {
                long start = i * chunkSize;
                long end = (i == chunkCount - 1) ? totalBytes - 1 : (start + chunkSize - 1);
                chunks.Add(new ChunkMetadata
                {
                    Index = i,
                    StartOffset = start,
                    EndOffset = end,
                    BytesDownloaded = 0
                });
            }

            var newMeta = new DownloadSessionMetadata
            {
                Url = url,
                TotalBytes = totalBytes,
                Chunks = chunks
            };

            // Preallocate sparse file size to prevent disk fragmentation
            if (File.Exists(partPath)) File.Delete(partPath);
            using (var fs = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                fs.SetLength(totalBytes);
            }

            SaveMetaToFile_NoLock(metaPath, newMeta);
            return newMeta;
        }
    }

    private void SaveMetaToFile_NoLock(string metaPath, DownloadSessionMetadata meta)
    {
        try
        {
            var json = JsonSerializer.Serialize(meta, NetworkJsonContext.Default.DownloadSessionMetadata);
            File.WriteAllText(metaPath, json);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to persist download metadata to {MetaPath}", metaPath);
        }
    }

    private async Task DownloadChunkWithRetryAsync(
        string url,
        string partPath,
        string metaPath,
        DownloadSessionMetadata sessionMeta,
        ChunkMetadata chunk,
        Action<int> onBytesRead,
        ChunkedDownloadOptions options,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 65536;

        for (int attempt = 1; attempt <= options.MaxRetriesPerChunk; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long currentOffset;
            lock (_metaLock)
            {
                if (chunk.IsCompleted) return;
                currentOffset = chunk.StartOffset + chunk.BytesDownloaded;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(currentOffset, chunk.EndOffset);
                request.Headers.AcceptEncoding.Clear();
                if (url.Contains("gamebanana.com", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Referrer = new Uri("https://gamebanana.com/");
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.PartialContent)
                {
                    throw new HttpRequestException($"Server returned unexpected status code {response.StatusCode} for range request.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var fileStream = new FileStream(partPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, bufferSize, useAsync: true);
                fileStream.Position = currentOffset;

                var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                try
                {
                    int bytesRead;
                    long unpersistedBytes = 0;

                    while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);

                        lock (_metaLock)
                        {
                            chunk.BytesDownloaded += bytesRead;
                        }

                        onBytesRead(bytesRead);
                        unpersistedBytes += bytesRead;

                        // Persist metadata every 2 MB to bound resume drift
                        if (unpersistedBytes >= 2 * 1024 * 1024)
                        {
                            lock (_metaLock)
                            {
                                SaveMetaToFile_NoLock(metaPath, sessionMeta);
                            }
                            unpersistedBytes = 0;
                        }
                    }

                    lock (_metaLock)
                    {
                        SaveMetaToFile_NoLock(metaPath, sessionMeta);
                    }

                    return; // Chunk completed
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < options.MaxRetriesPerChunk)
            {
                _logger.Warning(ex, "Chunk {Index} failed on attempt {Attempt}/{Max}. Retrying after delay...",
                    chunk.Index, attempt, options.MaxRetriesPerChunk);
                await Task.Delay(1000 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new IOException($"Failed to download chunk {chunk.Index} after {options.MaxRetriesPerChunk} attempts.");
    }

    private async Task DownloadSingleStreamAsync(
        string url,
        string destinationFilePath,
        long? totalBytes,
        IProgress<DownloadProgressInfo>? progress,
        ChunkedDownloadOptions options,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 65536;
        var partPath = destinationFilePath + ".part";

        long totalDownloaded = 0;
        if (File.Exists(partPath))
        {
            totalDownloaded = new FileInfo(partPath).Length;
        }

        for (int attempt = 1; attempt <= options.MaxRetriesPerChunk; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (totalDownloaded > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(totalDownloaded, null);
                    request.Headers.AcceptEncoding.Clear();
                }
                if (url.Contains("gamebanana.com", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Referrer = new Uri("https://gamebanana.com/");
                }

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                bool isPartial = response.StatusCode == HttpStatusCode.PartialContent;
                if (!isPartial && totalDownloaded > 0)
                {
                    totalDownloaded = 0;
                }

                if (!response.IsSuccessStatusCode && !isPartial)
                {
                    throw new HttpRequestException($"Download failed with status {response.StatusCode}.");
                }

                long? effectiveTotal = totalBytes;
                if (isPartial && response.Content.Headers.ContentRange?.Length.HasValue == true)
                {
                    effectiveTotal = response.Content.Headers.ContentRange.Length.Value;
                }
                else if (response.Content.Headers.ContentLength.HasValue)
                {
                    effectiveTotal = isPartial ? totalDownloaded + response.Content.Headers.ContentLength.Value : response.Content.Headers.ContentLength.Value;
                }

                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var fileMode = totalDownloaded > 0 && isPartial ? FileMode.Append : FileMode.Create;
                await using var fileStream = new FileStream(partPath, fileMode, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);

                var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
                var stopwatch = Stopwatch.StartNew();
                long bytesSinceLastTick = 0;
                var lastTickTime = stopwatch.Elapsed;

                try
                {
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);

                        totalDownloaded += bytesRead;
                        bytesSinceLastTick += bytesRead;

                        var now = stopwatch.Elapsed;
                        var elapsedSeconds = (now - lastTickTime).TotalSeconds;
                        if (elapsedSeconds >= options.SpeedEvaluationInterval.TotalSeconds)
                        {
                            var speed = bytesSinceLastTick / elapsedSeconds;
                            bytesSinceLastTick = 0;
                            lastTickTime = now;

                            int? percent = effectiveTotal.HasValue && effectiveTotal.Value > 0
                                ? (int)((double)totalDownloaded / effectiveTotal.Value * 100)
                                : null;

                            progress?.Report(new DownloadProgressInfo(totalDownloaded, effectiveTotal, speed, percent, 1));
                        }
                    }

                    // Final notification
                    int? finalPercent = effectiveTotal.HasValue && effectiveTotal.Value > 0 ? 100 : null;
                    progress?.Report(new DownloadProgressInfo(totalDownloaded, effectiveTotal, 0, finalPercent, 0));

                    await fileStream.DisposeAsync().ConfigureAwait(false);
                    await contentStream.DisposeAsync().ConfigureAwait(false);
                    if (File.Exists(destinationFilePath)) File.Delete(destinationFilePath);
                    File.Move(partPath, destinationFilePath, overwrite: true);
                    return;
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < options.MaxRetriesPerChunk)
            {
                _logger.Warning(ex, "Single-stream download failed on attempt {Attempt}/{Max}. Retrying...", attempt, options.MaxRetriesPerChunk);
                await Task.Delay(1000 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new IOException($"Single-stream download failed after {options.MaxRetriesPerChunk} attempts.");
    }

    private async Task RunProgressMonitorAsync(
        DownloadSessionMetadata sessionMeta,
        long totalBytes,
        IProgress<DownloadProgressInfo>? progress,
        Func<long> getAndResetSpeedBytes,
        ChunkedDownloadOptions options,
        CancellationToken cancellationToken)
    {
        if (progress == null) return;

        var stopwatch = Stopwatch.StartNew();
        var lastTime = stopwatch.Elapsed;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.SpeedEvaluationInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var now = stopwatch.Elapsed;
            var elapsedSec = (now - lastTime).TotalSeconds;
            if (elapsedSec <= 0) continue;

            long bytesThisInterval = getAndResetSpeedBytes();
            double speed = bytesThisInterval / elapsedSec;
            lastTime = now;

            long totalReceived;
            int activeWorkers;
            lock (_metaLock)
            {
                totalReceived = sessionMeta.Chunks.Sum(c => c.BytesDownloaded);
                activeWorkers = sessionMeta.Chunks.Count(c => !c.IsCompleted);
            }

            int percent = totalBytes > 0 ? (int)((double)totalReceived / totalBytes * 100) : 0;
            progress.Report(new DownloadProgressInfo(totalReceived, totalBytes, speed, percent, activeWorkers));
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}