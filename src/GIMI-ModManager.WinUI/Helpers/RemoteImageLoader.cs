using System.Collections.Concurrent;
using System.Net;
using System.Text;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GIMI_ModManager.WinUI.Helpers;

/// <summary>
/// Loads remote images via HttpClient and caches them in memory.
/// BitmapImage.UriSource does not reliably load HTTPS URLs in WinUI 3 Desktop,
/// so we download bytes first, then use SetSourceAsync on the UI thread.
/// </summary>
public static class RemoteImageLoader
{
    private static readonly ILogger Logger = Log.ForContext(typeof(RemoteImageLoader));
    private static readonly HttpClient Http = CreateHttpClient();

    // Bounded in-flight concurrency so a large list (e.g. GameBanana thumbnails) does not
    // fire an unbounded number of simultaneous downloads.
    private const int MaxConcurrentDownloads = 8;
    private static readonly SemaphoreSlim DownloadGate = new(MaxConcurrentDownloads, MaxConcurrentDownloads);

    // Deduplicates in-flight downloads per URL: many items sharing the same image
    // (or re-scrolling over the same row) share a single download.
    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]>>> InFlightDownloads = new(StringComparer.Ordinal);

    // Bounded BitmapImage cache. SizeLimit is in "size units"; every entry costs 1,
    // so at most ~MaxCachedBitmaps images are kept in memory.
    private const int MaxCachedBitmaps = 96;
    private static readonly MemoryCache BitmapCache = new(new MemoryCacheOptions
    {
        SizeLimit = MaxCachedBitmaps,
        CompactionPercentage = 0.25
    });

    // Disk-backed cache of the raw downloaded bytes so thumbnails survive app
    // restarts without re-downloading. Bounded by MaxDiskCacheBytes; the oldest
    // files are pruned when the budget is exceeded.
    private static readonly string DiskCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JASM", "RemoteImageCache");
    // The image cache budget is its own app-scoped setting (thumbnail/preview
    // images are shared across games), clamped to a sensible range.
    private const long MinImageCacheBytes = 32L * 1024 * 1024;
    private const long MaxImageCacheBytes = 1024L * 1024 * 1024;
    private const int MaxDiskCacheFiles = 4096;
    private static readonly object DiskCacheGate = new();
    private static DateTime _lastDiskPruneUtc = DateTime.MinValue;
    private static bool _diskCacheInitialized;

    // Short-lived failure cooldown so a broken URL is not re-requested on every scroll pass.
    private static readonly ConcurrentDictionary<string, DateTimeOffset> FailedUrls = new(StringComparer.Ordinal);
    private static readonly TimeSpan FailedUrlCooldown = TimeSpan.FromMinutes(1);
    private const int FailedUrlsPruneThreshold = 512;

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("JASM-ModManager");
        return client;
    }

    /// <summary>
    /// Load an image from a remote URL. Returns a cached BitmapImage if available.
    /// The returned BitmapImage is always created on the UI thread.
    /// </summary>
    public static async Task<BitmapImage?> LoadAsync(string? url, int decodePixelWidth = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var cacheKey = BuildCacheKey(url, null);
        if (BitmapCache.TryGetValue(cacheKey, out BitmapImage? cached))
            return cached;

        byte[] bytes;
        try
        {
            bytes = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to download image from {Url}", url);
            return null;
        }

        return await CreateBitmapOnUiThreadAsync(url, bytes, decodePixelWidth, cacheKey, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Fire-and-forget image loading. Sets the callback with the loaded image once ready.
    /// Must be called from the UI thread; the callback is always invoked on the UI thread.
    /// </summary>
    /// <param name="cacheKeySuffix">Optional suffix appended to the cache key (not the URL).
    /// Use this to cache the same URL at different decode sizes.</param>
    public static void LoadInto(string? url, int decodePixelWidth, Action<BitmapImage?> callback,
        string? cacheKeySuffix = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(url))
        {
            callback(null);
            return;
        }

        var cacheKey = BuildCacheKey(url, cacheKeySuffix);
        if (BitmapCache.TryGetValue(cacheKey, out BitmapImage? cached))
        {
            callback(cached);
            return;
        }

        _ = Task.Run(async () =>
        {
            var bmp = await LoadAsync(url, decodePixelWidth, cancellationToken).ConfigureAwait(false);

            var dispatcher = App.MainWindow?.DispatcherQueue;
            if (dispatcher is null)
            {
                callback(bmp);
                return;
            }

            dispatcher.TryEnqueue(() => callback(bmp));
        }, cancellationToken);
    }

    /// <summary>
    /// Loads a small, CPU-blurred variant of a remote image. Used for NSFW card
    /// thumbnails so the content is genuinely unrecognizable instead of merely
    /// covered by an overlay. The blurred bitmap is cached like any other image.
    /// Must be called from the UI thread; the callback is invoked on the UI thread.
    /// </summary>
    public static void LoadIntoBlurred(string? url, int decodePixelWidth, Action<BitmapImage?> callback,
        string? cacheKeySuffix = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(url))
        {
            callback(null);
            return;
        }

        var cacheKey = BuildCacheKey(url, cacheKeySuffix ?? "_blur");
        if (BitmapCache.TryGetValue(cacheKey, out BitmapImage? cached))
        {
            callback(cached);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
                var blurredPng = await CreateBlurredPngAsync(bytes, decodePixelWidth, cancellationToken)
                    .ConfigureAwait(false);

                var dispatcher = App.MainWindow?.DispatcherQueue;
                if (dispatcher is null || blurredPng is null)
                {
                    callback(null);
                    return;
                }

                dispatcher.TryEnqueue(async () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        callback(null);
                        return;
                    }

                    try
                    {
                        var bmp = await CreateBitmapFromBytes(blurredPng, 0).ConfigureAwait(true);
                        if (bmp is not null)
                            BitmapCache.Set(cacheKey, bmp, new MemoryCacheEntryOptions { Size = 1 });
                        callback(bmp);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to create blurred BitmapImage for {Url}", url);
                        callback(null);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Failed to create blurred thumbnail for {Url}", url);
                callback(null);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Decodes the source bytes at a small size and applies a box blur, returning
    /// the result re-encoded as PNG. Runs off the UI thread.
    /// </summary>
    private static async Task<byte[]?> CreateBlurredPngAsync(byte[] sourceBytes, int targetWidth,
        CancellationToken cancellationToken)
    {
        try
        {
            using var inStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(inStream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(sourceBytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
            }

            inStream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(inStream);

            var sourceWidth = (int)decoder.PixelWidth;
            var sourceHeight = (int)decoder.PixelHeight;
            if (sourceWidth <= 0 || sourceHeight <= 0)
                return null;

            var scale = Math.Min(1.0, (double)targetWidth / sourceWidth);
            var width = Math.Max(1, (int)(sourceWidth * scale));
            var height = Math.Max(1, (int)(sourceHeight * scale));

            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)width,
                ScaledHeight = (uint)height
            };

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            var pixels = pixelData.DetachPixelData();
            ApplyBoxBlur(pixels, width, height, blurRadius: 9);

            using var outStream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outStream);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)width,
                (uint)height,
                96,
                96,
                pixels);
            await encoder.FlushAsync();

            outStream.Seek(0);
            using var reader = new DataReader(outStream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)outStream.Size);
            var result = new byte[outStream.Size];
            reader.ReadBytes(result);
            return result;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to blur image");
            return null;
        }
    }

    /// <summary>
    /// Two iterations of a separable box blur (horizontal + vertical passes).
    /// Operates on BGRA8 pixels.
    /// </summary>
    private static void ApplyBoxBlur(byte[] pixels, int width, int height, int blurRadius)
    {
        var buffer = new byte[pixels.Length];

        for (var iteration = 0; iteration < 2; iteration++)
        {
            // Horizontal pass: buffer = blur(pixels)
            for (var y = 0; y < height; y++)
            {
                var rowStart = y * width;
                for (var x = 0; x < width; x++)
                {
                    var sumA = 0;
                    var sumB = 0;
                    var sumG = 0;
                    var sumR = 0;
                    var count = 0;
                    var x0 = Math.Max(0, x - blurRadius);
                    var x1 = Math.Min(width - 1, x + blurRadius);
                    for (var xx = x0; xx <= x1; xx++)
                    {
                        var i = (rowStart + xx) * 4;
                        sumA += pixels[i + 3];
                        sumB += pixels[i];
                        sumG += pixels[i + 1];
                        sumR += pixels[i + 2];
                        count++;
                    }

                    var o = (rowStart + x) * 4;
                    buffer[o] = (byte)(sumB / count);
                    buffer[o + 1] = (byte)(sumG / count);
                    buffer[o + 2] = (byte)(sumR / count);
                    buffer[o + 3] = (byte)(sumA / count);
                }
            }

            // Vertical pass: pixels = blur(buffer)
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    var sumA = 0;
                    var sumB = 0;
                    var sumG = 0;
                    var sumR = 0;
                    var count = 0;
                    var y0 = Math.Max(0, y - blurRadius);
                    var y1 = Math.Min(height - 1, y + blurRadius);
                    for (var yy = y0; yy <= y1; yy++)
                    {
                        var i = (yy * width + x) * 4;
                        sumA += buffer[i + 3];
                        sumB += buffer[i];
                        sumG += buffer[i + 1];
                        sumR += buffer[i + 2];
                        count++;
                    }

                    var o = (y * width + x) * 4;
                    pixels[o] = (byte)(sumB / count);
                    pixels[o + 1] = (byte)(sumG / count);
                    pixels[o + 2] = (byte)(sumR / count);
                    pixels[o + 3] = (byte)(sumA / count);
                }
            }
        }
    }

    private static string BuildCacheKey(string url, string? cacheKeySuffix) =>
        cacheKeySuffix is null ? url : url + cacheKeySuffix;

    private static async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        // Serve from disk first; a previously failed URL may still have a cached copy.
        var diskBytes = TryReadDiskCache(url);
        if (diskBytes != null)
            return diskBytes;

        if (FailedUrls.TryGetValue(url, out var failedAt) && failedAt > DateTimeOffset.UtcNow)
            throw new IOException($"Image download previously failed for {url}");

        // Share one download for every concurrent request of the same URL. The Lazy
        // factory deliberately ignores the caller's token: a shared download is not
        // aborted just because one subscriber went away. Callers observe cancellation
        // while waiting (WaitAsync) instead.
        var lazy = InFlightDownloads.GetOrAdd(url,
            static (u, _) => new Lazy<Task<byte[]>>(() => DownloadCoreAsync(u), LazyThreadSafetyMode.ExecutionAndPublication),
            url);

        try
        {
            var bytes = await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
            WriteDiskCache(url, bytes);
            return bytes;
        }
        finally
        {
            // Remove the completed/failed entry so later calls can retry.
            InFlightDownloads.TryRemove(new KeyValuePair<string, Lazy<Task<byte[]>>>(url, lazy));
        }
    }

    private static string GetDiskCachePath(string url)
    {
        var hash = System.IO.Hashing.XxHash64.Hash(Encoding.UTF8.GetBytes(url));
        return Path.Combine(DiskCacheDirectory, Convert.ToHexString(hash) + ".img");
    }

    /// <summary>
    /// Reads the app-scoped image cache limit (in MB) from settings and clamps it
    /// to 32-1024 MB. Called on the throttled prune path, so setting changes
    /// apply within minutes.
    /// </summary>
    private static long GetImageCacheByteBudget()
    {
        try
        {
            var settings = App.GetService<ILocalSettingsService>()
                .ReadSetting<ImageCacheSettings>(ImageCacheSettings.Key, SettingScope.App);
            var sizeMb = settings?.MaxImageCacheMb ?? ImageCacheSettings.DefaultMaxImageCacheMb;
            var budget = sizeMb * 1024L * 1024L;
            return Math.Clamp(budget, MinImageCacheBytes, MaxImageCacheBytes);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to read image cache limit; using default image cache budget");
            return MaxImageCacheBytes;
        }
    }

    /// <summary>
    /// Total size of the on-disk image cache, for the Settings page.
    /// </summary>
    public static long GetDiskCacheSizeBytes()
    {
        try
        {
            EnsureDiskCacheInitialized();
            return Directory.Exists(DiskCacheDirectory)
                ? Directory.GetFiles(DiskCacheDirectory).Sum(f => new FileInfo(f).Length)
                : 0;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to measure image disk cache");
            return 0;
        }
    }

    /// <summary>
    /// Empties the on-disk image cache (thumbnails are re-downloaded on demand).
    /// </summary>
    public static void ClearDiskCache()
    {
        try
        {
            EnsureDiskCacheInitialized();
            foreach (var file in Directory.GetFiles(DiskCacheDirectory))
            {
                DeleteDiskCacheFile(file);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to clear image disk cache");
        }
    }

    private static void EnsureDiskCacheInitialized()
    {
        lock (DiskCacheGate)
        {
            if (_diskCacheInitialized)
                return;

            _diskCacheInitialized = true;
            try
            {
                Directory.CreateDirectory(DiskCacheDirectory);
                PruneDiskCacheIfNeeded(force: true);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Failed to initialize image disk cache");
            }
        }
    }

    private static byte[]? TryReadDiskCache(string url)
    {
        try
        {
            EnsureDiskCacheInitialized();

            var path = GetDiskCachePath(url);
            if (!File.Exists(path))
                return null;

            var bytes = File.ReadAllBytes(path);

            // Guard against corrupt/truncated files (e.g. a crash during a
            // previous write): evict and fall through to the network.
            if (!IsPlausibleImage(bytes))
            {
                Logger.Debug("Evicting corrupt image from disk cache: {Path}", path);
                DeleteDiskCacheFile(path);
                return null;
            }

            // Touch the file so LRU eviction reflects real usage, not just the
            // moment the image was first downloaded.
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromMinutes(1))
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }

            return bytes;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to read image from disk cache for {Url}", url);
            return null;
        }
    }

    private static void WriteDiskCache(string url, byte[] bytes)
    {
        try
        {
            EnsureDiskCacheInitialized();

            // Write to a temp file first and move it into place so a crash in
            // the middle of a write can never leave a truncated "valid" entry.
            var path = GetDiskCachePath(url);
            var tempPath = path + ".tmp";
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, path, overwrite: true);

            PruneDiskCacheIfNeeded();
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to write image to disk cache for {Url}", url);
        }
    }

    private static void DeleteDiskCache(string url)
    {
        try
        {
            DeleteDiskCacheFile(GetDiskCachePath(url));
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to delete disk cache entry for {Url}", url);
        }
    }

    private static void DeleteDiskCacheFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to delete disk cache file {Path}", path);
        }
    }

    private static bool IsPlausibleImage(byte[] bytes)
    {
        if (bytes.Length < 12)
            return false;

        // JPEG
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return true;
        // PNG
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return true;
        // GIF
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
            return true;
        // BMP
        if (bytes[0] == 0x42 && bytes[1] == 0x4D)
            return true;
        // WebP (RIFF .... WEBP)
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return true;

        return false;
    }

    private static void PruneDiskCacheIfNeeded(bool force = false)
    {
        lock (DiskCacheGate)
        {
            if (!force && DateTime.UtcNow - _lastDiskPruneUtc < TimeSpan.FromMinutes(5))
                return;

            _lastDiskPruneUtc = DateTime.UtcNow;

            try
            {
                if (!Directory.Exists(DiskCacheDirectory))
                    return;

                // Remove leftover temp files from interrupted atomic writes.
                var staleThreshold = DateTime.UtcNow.AddDays(-1);
                foreach (var tmp in Directory.GetFiles(DiskCacheDirectory, "*.tmp"))
                {
                    var tmpInfo = new FileInfo(tmp);
                    if (tmpInfo.LastWriteTimeUtc < staleThreshold)
                    {
                        DeleteDiskCacheFile(tmp);
                    }
                }

                var files = Directory.GetFiles(DiskCacheDirectory)
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                var budget = GetImageCacheByteBudget();
                var total = files.Sum(f => f.Length);
                if (total <= budget && files.Count <= MaxDiskCacheFiles)
                    return;

                // Delete oldest files until both the byte budget and the file
                // count budget fit again.
                foreach (var file in files.AsEnumerable().Reverse())
                {
                    if (total <= budget && files.Count <= MaxDiskCacheFiles)
                        break;

                    total -= file.Length;
                    files.Remove(file);
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug(ex, "Failed to delete disk cache entry {Path}", file.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Failed to prune image disk cache");
            }
        }
    }

    private static async Task<byte[]> DownloadCoreAsync(string url)
    {
        await DownloadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (FailedUrls.TryAdd(url, DateTimeOffset.UtcNow) && FailedUrls.Count > FailedUrlsPruneThreshold)
                PruneFailedUrls();

            Logger.Debug(ex, "Image download failed for {Url}", url);
            throw;
        }
        finally
        {
            DownloadGate.Release();
        }
    }

    private static void PruneFailedUrls()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in FailedUrls)
        {
            if (now - pair.Value >= FailedUrlCooldown)
                FailedUrls.TryRemove(pair);
        }
    }

    private static Task<BitmapImage?> CreateBitmapOnUiThreadAsync(string url, byte[] bytes, int decodePixelWidth,
        string cacheKey, CancellationToken cancellationToken)
    {
        var dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher is null)
        {
            Logger.Warning("No main window dispatcher available; cannot create BitmapImage for {Url}", url);
            return Task.FromResult<BitmapImage?>(null);
        }

        var tcs = new TaskCompletionSource<BitmapImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueued = dispatcher.TryEnqueue(async () =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetResult(null);
                return;
            }

            try
            {
                var bmp = await CreateBitmapFromBytes(bytes, decodePixelWidth).ConfigureAwait(true);
                if (bmp is not null)
                    BitmapCache.Set(cacheKey, bmp, new MemoryCacheEntryOptions { Size = 1 });
                tcs.TrySetResult(bmp);
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to create BitmapImage on UI thread for {Url}", url);
                // The bytes could not be decoded; evict them so we retry with a
                // fresh download instead of serving a broken entry forever.
                DeleteDiskCache(url);
                tcs.TrySetResult(null);
            }
        });

        if (!enqueued)
            tcs.TrySetResult(null);

        return tcs.Task;
    }

    private static async Task<BitmapImage?> CreateBitmapFromBytes(byte[] bytes, int decodePixelWidth)
    {
        using var stream = new InMemoryRandomAccessStream();
        using var writer = new DataWriter(stream.GetOutputStreamAt(0));
        writer.WriteBytes(bytes);
        await writer.StoreAsync();
        await writer.FlushAsync();
        stream.Seek(0);

        var bmp = new BitmapImage();
        if (decodePixelWidth > 0)
        {
            bmp.DecodePixelWidth = decodePixelWidth;
            bmp.DecodePixelType = DecodePixelType.Logical;
        }

        await bmp.SetSourceAsync(stream);
        return bmp;
    }
}