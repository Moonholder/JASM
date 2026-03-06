using System.Collections.Concurrent;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;
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
    private static readonly HttpClient Http = new();
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();

    /// <summary>
    /// Load an image from a remote URL. Returns a cached BitmapImage if available.
    /// Must be called from the UI thread (or dispatched to it).
    /// </summary>
    public static async Task<BitmapImage?> LoadAsync(string? url, int decodePixelWidth = 0)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        // Check cache
        if (Cache.TryGetValue(url, out var cached))
            return cached;

        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);

            // SetSourceAsync must run on the UI thread
            var tcs = new TaskCompletionSource<BitmapImage?>();
            var dispatcher = DispatcherQueue.GetForCurrentThread();

            // If already on UI thread, use it; otherwise, enqueue
            if (dispatcher != null)
            {
                var bmp = await CreateBitmapFromBytes(bytes, decodePixelWidth);
                if (bmp != null) Cache.TryAdd(url, bmp);
                return bmp;
            }

            // We're on a background thread – dispatch to UI
            App.MainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var bmp = await CreateBitmapFromBytes(bytes, decodePixelWidth);
                    if (bmp != null) Cache.TryAdd(url, bmp);
                    tcs.TrySetResult(bmp);
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to create BitmapImage on UI thread for {Url}", url);
                    tcs.TrySetResult(null);
                }
            });

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to download image from {Url}", url);
            return null;
        }
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

    /// <summary>
    /// Fire-and-forget image loading. Sets the callback with the loaded image once ready.
    /// Must be called from UI thread.
    /// </summary>
    /// <param name="cacheKeySuffix">Optional suffix appended to the cache key (not the URL).
    /// Use this to cache the same URL at different decode sizes.</param>
    public static void LoadInto(string? url, int decodePixelWidth, Action<BitmapImage?> callback, string? cacheKeySuffix = null)
    {
        if (string.IsNullOrEmpty(url))
        {
            callback(null);
            return;
        }

        var cacheKey = cacheKeySuffix != null ? url + cacheKeySuffix : url;

        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            callback(cached);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                App.MainWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var bmp = await CreateBitmapFromBytes(bytes, decodePixelWidth);
                        if (bmp != null) Cache.TryAdd(cacheKey, bmp);
                        callback(bmp);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to create BitmapImage for {Url}", url);
                        callback(null);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to download image {Url}", url);
                callback(null);
            }
        });
    }
}