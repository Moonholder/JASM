using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using GIMI_ModManager.Core.Services.GameBanana.ApiModels;
using GIMI_ModManager.Core.Services.GameBanana.Models;
using Polly;
using Polly.RateLimiting;
using Polly.Registry;
using Serilog;

namespace GIMI_ModManager.Core.Services.GameBanana;

public sealed class ApiGameBananaClient(
    ILogger logger,
    HttpClient httpClient,
    ResiliencePipelineProvider<string> resiliencePipelineProvider)
    : IApiGameBananaClient
{
    private readonly ILogger _logger = logger.ForContext<ApiGameBananaClient>();
    private readonly HttpClient _httpClient = httpClient;
    private readonly ResiliencePipeline _resiliencePipeline = resiliencePipelineProvider.GetPipeline(HttpClientName);
    public const string HttpClientName = nameof(IApiGameBananaClient);

    private const string DownloadUrl = "https://gamebanana.com/dl/";
    private const string BaseApiUrl = "https://gamebanana.com/apiv13/";
    private const string ApiUrl = BaseApiUrl + "Mod/";
    private const string HealthCheckUrl = "https://api.gamebanana.com/Core/Item/Data/AllowedItemTypes";

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(HealthCheckUrl, cancellationToken).ConfigureAwait(false);

        foreach (var (key, value) in response.Headers)
        {
            if (key.Contains("Deprecation", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Deprecated", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning("GameBanana API is deprecated: {Key}={Value}", key, value);
                Debugger.Break();
                break;
            }
        }

        return response.StatusCode == HttpStatusCode.OK;
    }

    public async Task<ApiModProfile?> GetModProfileAsync(GbModId modId, string modelName = "Mod", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modId);

        var modPageApiUrl = GetModelUrl(modId, modelName, "ProfilePage");
        return await SendAndDeserializeAsync(modPageApiUrl, Serialization.GameBananaApiJsonContext.Default.ApiModProfile, cancellationToken, allowNotFound: true)
            .ConfigureAwait(false);
    }

    public async Task<ApiModFilesInfo?> GetModFilesInfoAsync(GbModId modId, string modelName = "Mod",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modId);

        var downloadsApiUrl = GetModelUrl(modId, modelName, "DownloadPage");
        return await SendAndDeserializeAsync(downloadsApiUrl, Serialization.GameBananaApiJsonContext.Default.ApiModFilesInfo, cancellationToken, allowNotFound: true)
            .ConfigureAwait(false);
    }

    public async Task<ApiModFileInfo?> GetModFileInfoAsync(GbModId modId, GbModFileId modFileId, string modelName = "Mod",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modFileId);

        var modFilesInfo = await GetModFilesInfoAsync(modId, modelName, cancellationToken).ConfigureAwait(false);

        return modFilesInfo?.Files.FirstOrDefault(x => x.FileId.ToString() == modFileId);
    }

    public async Task<bool> ModFileExists(GbModFileId modFileId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modFileId);

        var requestUrl = GetAltUrlForModInfo(modFileId);

        using var response = await SendRequest(requestUrl, cancellationToken).ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return !content.Contains("error:", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri GetAltUrlForModInfo(GbModFileId modFileId)
    {
        return new Uri(
            $"https://api.gamebanana.com/Core/Item/Data?itemid={modFileId}&itemtype=File&fields=file");
    }

    public async Task DownloadModAsync(GbModFileId modFileId, FileStream destinationFile, IProgress<int>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modFileId, nameof(modFileId));
        ArgumentNullException.ThrowIfNull(destinationFile);
        var downloadUrl = DownloadUrl + modFileId;

        await DownloadFromUrlAsync(downloadUrl, destinationFile, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task DownloadModByUrlAsync(string downloadUrl, FileStream destinationFile, IProgress<int>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadUrl, nameof(downloadUrl));
        ArgumentNullException.ThrowIfNull(destinationFile);

        await DownloadFromUrlAsync(downloadUrl, destinationFile, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadFromUrlAsync(string downloadUrl, FileStream destinationFile, IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        long totalRead = destinationFile.Length;
        if (totalRead > 0)
        {
            destinationFile.Seek(totalRead, SeekOrigin.Begin);
        }
        long? totalSizeBytes = null;
        int lastPercent = -1;
        int maxRetries = 5;
        int attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                if (totalRead > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(totalRead, null);
                }

                using var response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new InvalidOperationException("Mod not found.");

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    destinationFile.Position = 0;
                    destinationFile.SetLength(0);
                    totalRead = 0;
                    lastPercent = -1;
                    continue;
                }

                bool isPartial = response.StatusCode == HttpStatusCode.PartialContent;
                if (!isPartial && totalRead > 0)
                {
                    destinationFile.Position = 0;
                    destinationFile.SetLength(0);
                    totalRead = 0;
                    lastPercent = -1;
                }

                if (response.StatusCode != HttpStatusCode.OK && response.StatusCode != HttpStatusCode.PartialContent)
                {
                    throw new HttpRequestException(
                        $"Failed to download mod from GameBanana. Reason: {response.ReasonPhrase}");
                }

                if (totalSizeBytes == null || !isPartial)
                {
                    if (isPartial && response.Content.Headers.ContentRange?.Length.HasValue == true)
                    {
                        totalSizeBytes = response.Content.Headers.ContentRange.Length.Value;
                    }
                    else if (isPartial && response.Content.Headers.ContentLength.HasValue)
                    {
                        totalSizeBytes = totalRead + response.Content.Headers.ContentLength.Value;
                    }
                    else
                    {
                        totalSizeBytes = response.Content.Headers.ContentLength;
                    }
                }

                if (totalRead > 0 && totalSizeBytes.HasValue && totalSizeBytes.Value > 0 && progress != null)
                {
                    var initialPercent = (int)((double)totalRead / totalSizeBytes.Value * 100);
                    if (initialPercent != lastPercent)
                    {
                        progress.Report(initialPercent);
                        lastPercent = initialPercent;
                    }
                }

                await using var downloadStream =
                    await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

                // Rent a 128KB buffer for blazing-fast network-to-disk throughput
                var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(131072);
                try
                {
                    int bytesRead;
                    // Read directly from the network stream in chunks and report progress naturally
                    while ((bytesRead = await downloadStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await destinationFile.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                        totalRead += bytesRead;

                        if (totalSizeBytes.HasValue && totalSizeBytes.Value > 0 && progress != null)
                        {
                            var percent = (int)((double)totalRead / totalSizeBytes.Value * 100);
                            // De-duplicate progress reports
                            if (percent != lastPercent)
                            {
                                progress.Report(percent);
                                lastPercent = percent;
                            }
                        }
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }

                break;
            }
            catch (Exception ex) when (attempt < maxRetries &&
                                      (ex is System.IO.IOException ||
                                       ex is HttpRequestException ||
                                       ex is System.Net.Sockets.SocketException ||
                                       ex is TimeoutException ||
                                       (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)))
            {
                _logger.Warning(ex, "Download attempt {Attempt} failed for {Url}. Retrying in 2 seconds...", attempt, downloadUrl);
                await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
            }
        }

        await destinationFile.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static Uri GetModelUrl(GbModId modId, string modelName, string endpoint)
    {
        return new Uri($"{BaseApiUrl}{modelName}/{modId}/{endpoint}");
    }

    private async Task<HttpResponseMessage> SendRequest(Uri downloadsApiUrl, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        HttpResponseMessage response;
        retry:
        try
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);

            if (IgnorePollyLimiterScope.IsIgnored)
            {
                response = await _httpClient.GetAsync(downloadsApiUrl, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Use anonymous state object to avoid closure allocation
                var state = new { url = downloadsApiUrl, httpClient = _httpClient };

                response = await _resiliencePipeline.ExecuteAsync(
                        async (context, token) => await context.httpClient.GetAsync(context.url, token)
                            .ConfigureAwait(false),
                        state, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (RateLimiterRejectedException e)
        {
            _logger.Debug("Rate limit exceeded, retrying after {retryAfter}", e.RetryAfter);
            var delay = e.RetryAfter ?? TimeSpan.FromSeconds(2);

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            goto retry;
        }

        if (!response.IsSuccessStatusCode && !(allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
        {
            _logger.Error("Failed to get mod info from GameBanana: {response} | Url: {Url}", response,
                downloadsApiUrl);
            throw new HttpRequestException(
                $"Failed to get mod info from GameBanana. Reason: {response?.ReasonPhrase ?? "Unknown"} | Url: {downloadsApiUrl}");
        }

        _logger.Debug("Response received {0} | {1}", DateTime.Now, downloadsApiUrl);

        return response;
    }

    private async Task<T?> SendAndDeserializeAsync<T>(
        Uri url,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken,
        bool allowNotFound = false) where T : class
    {
        const int maxAttempts = 2;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var response = await SendRequest(url, cancellationToken, allowNotFound: allowNotFound).ConfigureAwait(false);

            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                return null;

            if (response.StatusCode == HttpStatusCode.NoContent)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            if (bytes.Length == 0 || IsAllWhiteSpace(bytes))
            {
                _logger.Warning("Received empty or whitespace response from GameBanana API: {Url} (attempt {Attempt}/{Max})", url, attempt, maxAttempts);
                if (attempt < maxAttempts)
                {
                    await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                return null;
            }

            try
            {
                var apiResponse = JsonSerializer.Deserialize(bytes, jsonTypeInfo);
                return apiResponse;
            }
            catch (JsonException ex) when (attempt < maxAttempts)
            {
                _logger.Warning(ex, "Failed to parse JSON response from GameBanana API: {Url} (attempt {Attempt}/{Max}), retrying...", url, attempt, maxAttempts);
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                _logger.Error(ex, "Failed to deserialize GameBanana API response: {Url}", url);
                return null;
            }
        }

        return null;
    }

    private static bool IsAllWhiteSpace(ReadOnlySpan<byte> bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if (b is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
                return false;
        }
        return true;
    }

    public async Task<List<ApiCategoryItem>> GetCategoriesAsync(string parentCategoryId, CancellationToken cancellationToken = default)
    {
        var url = new Uri($"{ApiUrl.Replace("/Mod/", "/Mod/")}Categories?_idCategoryRow={parentCategoryId}&_sSort=a_to_z&_bShowEmpty=true");
        var result = await SendAndDeserializeAsync(url, Serialization.GameBananaApiJsonContext.Default.ListApiCategoryItem, cancellationToken)
            .ConfigureAwait(false);
        return result ?? [];
    }

    public async Task<List<ApiCategoryItem>> GetCategoriesForGameAsync(string gameId, CancellationToken cancellationToken = default)
    {
        var url = new Uri($"https://gamebanana.com/apiv13/Mod/Categories?_idGameRow={gameId}&_sSort=a_to_z&_nPerpage=50");
        var result = await SendAndDeserializeAsync(url, Serialization.GameBananaApiJsonContext.Default.ListApiCategoryItem, cancellationToken)
            .ConfigureAwait(false);
        return result ?? [];
    }

    public async Task<List<ApiModRecord>> GetGameSubfeedAsync(string gameId, string sort = "default", int page = 1,
        CancellationToken cancellationToken = default)
    {
        var sortParam = sort == "default" ? "" : $"&_sSort={sort}";
        var url = new Uri($"https://gamebanana.com/apiv13/Game/{gameId}/Subfeed?_nPage={page}{sortParam}");
        var result = await SendAndDeserializeAsync(url, Serialization.GameBananaApiJsonContext.Default.ApiPaginatedResponseApiModRecord, cancellationToken)
            .ConfigureAwait(false);
        return result?.Records ?? [];
    }

    public async Task<List<ApiModRecord>> GetModsByCategoryAsync(string categoryId,
        int page = 1, int perPage = 15, CancellationToken cancellationToken = default)
    {
        var url = new Uri(
            $"https://gamebanana.com/apiv13/Mod/Index?_nPerpage={perPage}&_aFilters%5BGeneric_Category%5D={categoryId}&_nPage={page}");
        var result = await SendAndDeserializeAsync(url, Serialization.GameBananaApiJsonContext.Default.ApiPaginatedResponseApiModRecord, cancellationToken)
            .ConfigureAwait(false);
        return result?.Records ?? [];
    }

    public async Task<List<ApiModRecord>> SearchModsAsync(string gameId, string query, string? modelName = null, int page = 1,
        CancellationToken cancellationToken = default)
    {
        var encodedQuery = Uri.EscapeDataString(query);
        var modelParam = string.IsNullOrWhiteSpace(modelName) ? "" : $"&_sModelName={modelName}";
        var url = new Uri(
            $"https://gamebanana.com/apiv13/Util/Search/Results?_sOrder=best_match&_idGameRow={gameId}&_sSearchString={encodedQuery}&_nPage={page}{modelParam}");
        var result = await SendAndDeserializeAsync(url, Serialization.GameBananaApiJsonContext.Default.ApiPaginatedResponseApiModRecord, cancellationToken)
            .ConfigureAwait(false);
        return result?.Records ?? [];
    }

    public async Task<List<ApiModUpdate>> GetModUpdatesAsync(string modId, string modelName = "Mod", CancellationToken cancellationToken = default)
    {
        var url = new Uri($"{BaseApiUrl}{modelName}/{modId}/Updates?_nPage=1&_nPerpage=10");
        var result = await SendAndDeserializeAsync(url, Serialization.GameBananaApiJsonContext.Default.ApiModUpdateList, cancellationToken, allowNotFound: true)
            .ConfigureAwait(false);
        return result?.Records ?? [];
    }
}