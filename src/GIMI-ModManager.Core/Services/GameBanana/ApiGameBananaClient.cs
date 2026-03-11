using System.Diagnostics;
using System.Net;
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
    private const string BaseApiUrl = "https://gamebanana.com/apiv11/";
    private const string ApiUrl = BaseApiUrl + "Mod/";
    private const string HealthCheckUrl = "https://gamebanana.com/apiv11";

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

        using var response = await SendRequest(modPageApiUrl, cancellationToken).ConfigureAwait(false);

        _logger.Debug("Got response from GameBanana: {response}", response.StatusCode);
        await using var contentStream =
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var apiResponse =
            await JsonSerializer.DeserializeAsync<ApiModProfile>(contentStream,
                Serialization.GameBananaApiJsonContext.Default.ApiModProfile, cancellationToken)
            .ConfigureAwait(false);


        if (apiResponse == null)
        {
            _logger.Error("Failed to deserialize GameBanana response: {content}", contentStream);
            throw new HttpRequestException(
                $"Failed to deserialize GameBanana response. Reason: {response?.ReasonPhrase}");
        }

        return apiResponse;
    }

    public async Task<ApiModFilesInfo?> GetModFilesInfoAsync(GbModId modId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modId);

        var requestUrl = GetModFilesInfoUrl(modId);

        using var response = await SendRequest(requestUrl, cancellationToken).ConfigureAwait(false);

        _logger.Debug("Got response from GameBanana: {response}", response.StatusCode);
        await using var contentStream =
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var apiResponse =
            await JsonSerializer.DeserializeAsync<ApiModFilesInfo>(contentStream,
                Serialization.GameBananaApiJsonContext.Default.ApiModFilesInfo, cancellationToken)
            .ConfigureAwait(false);


        if (apiResponse == null)
        {
            _logger.Error("Failed to deserialize GameBanana response: {content}", contentStream);
            throw new HttpRequestException(
                $"Failed to deserialize GameBanana response. Reason: {response?.ReasonPhrase}");
        }

        return apiResponse;
    }

    public async Task<ApiModFileInfo?> GetModFileInfoAsync(GbModId modId, GbModFileId modFileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modFileId);

        var modFilesInfo = await GetModFilesInfoAsync(modId, cancellationToken).ConfigureAwait(false);

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


        using var response = await _httpClient
            .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("Mod not found.");

        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException(
                $"Failed to download mod from GameBanana. Reason: {response?.ReasonPhrase}");

        var contentLength = response.Content.Headers.ContentLength;

        await using var downloadStream =
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        if (contentLength is not null && progress is not null)
            _ = Task.Run(() => DownloadMonitor(contentLength.Value, destinationFile.Name, progress, cancellationToken),
                cancellationToken);

        await downloadStream.CopyToAsync(destinationFile, cancellationToken).ConfigureAwait(false);
    }

    private static async Task DownloadMonitor(long totalSizeBytes, string downloadFilePath, IProgress<int> progress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var file = new FileInfo(downloadFilePath);
            int lastProgress = -1;
            while (!cancellationToken.IsCancellationRequested)
            {
                file.Refresh();
                if (file.Exists)
                {
                    var fileSize = file.Length;
                    if (fileSize >= totalSizeBytes)
                        break;

                    var percent = (int)Math.Round((decimal)fileSize / (decimal)totalSizeBytes * 100);
                    if (percent != lastProgress)
                    {
                        progress.Report(percent);
                        lastProgress = percent;
                    }
                }
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif
        }
    }

    private Uri GetModFilesInfoUrl(GbModId gbModId)
    {
        return new Uri(ApiUrl + gbModId + "/DownloadPage");
    }

    private Uri GetModInfoUrl(GbModId gbModId)
    {
        return new Uri(ApiUrl + gbModId + "/ProfilePage");
    }

    private static Uri GetModelUrl(GbModId modId, string modelName, string endpoint)
    {
        return new Uri($"{BaseApiUrl}{modelName}/{modId}/{endpoint}");
    }

    private async Task<HttpResponseMessage> SendRequest(Uri downloadsApiUrl, CancellationToken cancellationToken)
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


        if (!response.IsSuccessStatusCode)
        {
            _logger.Error("Failed to get mod info from GameBanana: {response} | Url: {Url}", response,
                downloadsApiUrl);
            throw new HttpRequestException(
                $"Failed to get mod info from GameBanana. Reason: {response?.ReasonPhrase ?? "Unknown"} | Url: {downloadsApiUrl}");
        }

        _logger.Debug("Response received {0} | {1}", DateTime.Now, downloadsApiUrl);

        return response;
    }

    public async Task<List<ApiCategoryItem>> GetCategoriesAsync(string parentCategoryId, CancellationToken cancellationToken = default)
    {
        var url = new Uri($"{ApiUrl.Replace("/Mod/", "/Mod/")}Categories?_idCategoryRow={parentCategoryId}&_sSort=a_to_z&_bShowEmpty=true");

        using var response = await SendRequest(url, cancellationToken).ConfigureAwait(false);
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var result = await JsonSerializer.DeserializeAsync(contentStream,
            Serialization.GameBananaApiJsonContext.Default.ListApiCategoryItem, cancellationToken).ConfigureAwait(false);

        return result ?? new List<ApiCategoryItem>();
    }

    public async Task<List<ApiCategoryItem>> GetCategoriesForGameAsync(string gameId, CancellationToken cancellationToken = default)
    {
        var url = new Uri($"https://gamebanana.com/apiv11/Mod/Categories?_idGameRow={gameId}&_sSort=a_to_z&_nPerpage=50");

        using var response = await SendRequest(url, cancellationToken).ConfigureAwait(false);
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var result = await JsonSerializer.DeserializeAsync(contentStream,
            Serialization.GameBananaApiJsonContext.Default.ListApiCategoryItem, cancellationToken).ConfigureAwait(false);

        return result ?? new List<ApiCategoryItem>();
    }

    public async Task<List<ApiModRecord>> GetGameSubfeedAsync(string gameId, string sort = "default", int page = 1,
        CancellationToken cancellationToken = default)
    {
        var sortParam = sort == "default" ? "" : $"&_sSort={sort}";
        var url = new Uri($"https://gamebanana.com/apiv11/Game/{gameId}/Subfeed?_nPage={page}{sortParam}");

        using var response = await SendRequest(url, cancellationToken).ConfigureAwait(false);
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var result = await JsonSerializer.DeserializeAsync(contentStream,
            Serialization.GameBananaApiJsonContext.Default.ApiPaginatedResponseApiModRecord, cancellationToken).ConfigureAwait(false);

        return result?.Records ?? new List<ApiModRecord>();
    }

    public async Task<List<ApiModRecord>> GetModsByCategoryAsync(string categoryId,
        int page = 1, int perPage = 15, CancellationToken cancellationToken = default)
    {
        var url = new Uri(
            $"https://gamebanana.com/apiv11/Mod/Index?_nPerpage={perPage}&_aFilters%5BGeneric_Category%5D={categoryId}&_nPage={page}");

        using var response = await SendRequest(url, cancellationToken).ConfigureAwait(false);
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var result = await JsonSerializer.DeserializeAsync(contentStream,
            Serialization.GameBananaApiJsonContext.Default.ApiPaginatedResponseApiModRecord, cancellationToken).ConfigureAwait(false);

        return result?.Records ?? new List<ApiModRecord>();
    }

    public async Task<List<ApiModRecord>> SearchModsAsync(string gameId, string query, string? modelName = null, int page = 1,
        CancellationToken cancellationToken = default)
    {
        var encodedQuery = Uri.EscapeDataString(query);
        var modelParam = string.IsNullOrWhiteSpace(modelName) ? "" : $"&_sModelName={modelName}";
        var url = new Uri(
            $"https://gamebanana.com/apiv11/Util/Search/Results?_sOrder=best_match&_idGameRow={gameId}&_sSearchString={encodedQuery}&_nPage={page}{modelParam}");

        using var response = await SendRequest(url, cancellationToken).ConfigureAwait(false);
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var result = await JsonSerializer.DeserializeAsync(contentStream,
            Serialization.GameBananaApiJsonContext.Default.ApiPaginatedResponseApiModRecord, cancellationToken).ConfigureAwait(false);

        return result?.Records ?? new List<ApiModRecord>();
    }

    public async Task<List<ApiModUpdate>> GetModUpdatesAsync(string modId, string modelName = "Mod", CancellationToken cancellationToken = default)
    {
        var url = new Uri($"{BaseApiUrl}{modelName}/{modId}/Updates?_nPage=1&_nPerpage=10");

        using var response = await SendRequest(url, cancellationToken).ConfigureAwait(false);
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        var result = await JsonSerializer.DeserializeAsync(contentStream,
            Serialization.GameBananaApiJsonContext.Default.ApiModUpdateList, cancellationToken).ConfigureAwait(false);

        return result?.Records ?? new List<ApiModUpdate>();
    }
}