using System.Net.Http;
using System.Reflection;
using System.Xml.Linq;
using GIMI_ModManager.Core.Services.Network;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace GIMI_ModManager.WinUI.Services.AppManagement.Updating;

public enum UpdateCheckStatus
{
    Idle,
    Checking,
    Success,
    Failed
}

public sealed class UpdateChecker
{
    private readonly ILogger _logger;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly Notifications.NotificationManager _notificationManager;
    private readonly DynamicAppWebProxy _dynamicProxy;

    public Version CurrentVersion { get; private set; }
    public Version? LatestRetrievedVersion { get; private set; }
    public event EventHandler<NewVersionEventArgs>? NewVersionAvailable;
    private Version? _ignoredVersion;
    public Version? IgnoredVersion => _ignoredVersion;
    private bool DisableChecker;
    private CancellationTokenSource _cancellationTokenSource;

    /// <summary>
    /// Current status of the update check process.
    /// </summary>
    public UpdateCheckStatus CheckStatus { get; private set; } = UpdateCheckStatus.Idle;

    /// <summary>
    /// Raised when <see cref="CheckStatus"/> changes.
    /// </summary>
    public event EventHandler<UpdateCheckStatus>? CheckStatusChanged;

    private bool _isManualChecking;
    private const string ReleasesLatestApiUrl = "https://api.github.com/repos/Moonholder/JASM/releases/latest";
    private const string ReleasesLatestWebUrl = "https://github.com/Moonholder/JASM/releases/latest";
    private const string ReleasesAtomUrl = "https://github.com/Moonholder/JASM/releases.atom";

    public UpdateChecker(
        ILogger logger,
        ILocalSettingsService localSettingsService,
        Notifications.NotificationManager notificationManager,
        DynamicAppWebProxy dynamicProxy,
        CancellationToken cancellationToken = default)
    {
        _logger = logger.ForContext<UpdateChecker>();
        _localSettingsService = localSettingsService;
        _notificationManager = notificationManager;
        _dynamicProxy = dynamicProxy;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var version = Assembly.GetExecutingAssembly().GetName().Version;

        if (version is null)
        {
            _logger.Error("Failed to get current version");
            DisableChecker = true;
            CurrentVersion = new Version(0, 0, 0, 0);
            return;
        }

        CurrentVersion = version;
    }

    public async Task InitializeAsync()
    {
        var options = await _localSettingsService.ReadSettingAsync<UpdateCheckerOptions>(UpdateCheckerOptions.Key) ??
                      new UpdateCheckerOptions();
        if (options.IgnoreNewVersion is not null)
            _ignoredVersion = options.IgnoreNewVersion;

        if (options.IgnoreNewVersion is not null && options.IgnoreNewVersion <= CurrentVersion)
        {
            options.IgnoreNewVersion = null;
            await _localSettingsService.SaveSettingAsync(UpdateCheckerOptions.Key, options);
        }

        InitCheckerLoop(_cancellationTokenSource.Token);
    }

    public async Task IgnoreCurrentVersionAsync()
    {
        var options = await _localSettingsService.ReadSettingAsync<UpdateCheckerOptions>(UpdateCheckerOptions.Key) ??
                      new UpdateCheckerOptions();
        options.IgnoreNewVersion = LatestRetrievedVersion;
        await _localSettingsService.SaveSettingAsync(UpdateCheckerOptions.Key, options);
        _ignoredVersion = LatestRetrievedVersion;
        OnNewVersionAvailable(new Version());
    }

    /// <summary>
    /// Manually trigger an update check from UI. Always runs immediately regardless of polling interval.
    /// </summary>
    public async Task ManualCheckForUpdatesAsync()
    {
        if (_isManualChecking) return;
        _isManualChecking = true;
        try
        {
            await CheckForUpdatesAsync(CancellationToken.None);
        }
        finally
        {
            _isManualChecking = false;
        }
    }

    private void InitCheckerLoop(CancellationToken cancellationToken)
    {
        Task.Factory.StartNew(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
                try
                {
                    await CheckForUpdatesAsync(cancellationToken);
                    await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
                }
                catch (TaskCanceledException)
                {
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Failed to check for updates. Stopping Update checker");
                    break;
                }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (DisableChecker)
            return;

        SetCheckStatus(UpdateCheckStatus.Checking);

        var latestVersion = await GetLatestVersionAsync(cancellationToken);

        if (latestVersion is null)
        {
            _logger.Warning("No versions found, latestVersion is null");
            SetCheckStatus(UpdateCheckStatus.Failed);
            return;
        }

        if (CurrentVersion < latestVersion)
        {
            if (_ignoredVersion is not null && _ignoredVersion >= latestVersion)
            {
                SetCheckStatus(UpdateCheckStatus.Success);
                return;
            }
            LatestRetrievedVersion = latestVersion;
            OnNewVersionAvailable(latestVersion);
        }

        SetCheckStatus(UpdateCheckStatus.Success);
    }

    private async Task<Version?> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        // 1. Try authoritative direct GitHub /releases/latest endpoint (both API and Web redirect)
        var version = await TryFetchVersionFromLatestApiUrlAsync(ReleasesLatestApiUrl, cancellationToken);
        if (version is not null)
        {
            _logger.Debug("Got version via direct GitHub API (/releases/latest).");
            return version;
        }

        version = await TryFetchVersionFromLatestWebUrlAsync(ReleasesLatestWebUrl, cancellationToken);
        if (version is not null)
        {
            _logger.Information("Got version via direct GitHub Web redirect (/releases/latest).");
            return version;
        }

        // 2. Direct access failed, try mirror fallback with Web redirect and API
        _logger.Information("GitHub direct access failed, trying mirror fallback for version check...");
        try
        {
            var mirrors = await MirrorAddressSelector.GetAvailableMirrorsAsync(cancellationToken);
            foreach (var mirror in mirrors.Where(m => !string.IsNullOrEmpty(m.Address) && m.SupportsApiForward))
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Try mirror Web redirect first
                var mirrorWebUrl = mirror.Address.TrimEnd('/') + "/" + ReleasesLatestWebUrl;
                version = await TryFetchVersionFromLatestWebUrlAsync(mirrorWebUrl, cancellationToken);
                if (version is not null)
                {
                    _logger.Information("Got version via mirror {NodeName} (Web redirect).", mirror.NodeName);
                    return version;
                }

                // Try mirror API
                var mirrorApiUrl = mirror.Address.TrimEnd('/') + "/" + ReleasesLatestApiUrl;
                version = await TryFetchVersionFromLatestApiUrlAsync(mirrorApiUrl, cancellationToken);
                if (version is not null)
                {
                    _logger.Information("Got version via mirror {NodeName} (API).", mirror.NodeName);
                    return version;
                }
            }
        }
        catch (Exception e)
        {
            _logger.Warning(e, "Mirror fallback for version check failed.");
        }

        // 3. Last-resort fallback: Atom feed
        version = await TryFetchVersionFromAtomFeedAsync(ReleasesAtomUrl, cancellationToken);
        if (version is not null)
        {
            _logger.Information("Got version via direct GitHub Atom feed.");
            return version;
        }

        _logger.Warning("All sources (direct + mirrors) failed to fetch version info.");
        return null;
    }

    private async Task<Version?> TryFetchVersionFromLatestApiUrlAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = CreateHttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            var result = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccessStatusCode)
            {
                _logger.Debug("Failed to get version from API {Url}. Status: {StatusCode}", url, result.StatusCode);
                return null;
            }

            var text = await result.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize<GitHubRelease>(text, GitHubJsonContext.Default.GitHubRelease);
            if (release != null && !release.Prerelease)
            {
                var clean = release.TagName?.Trim().TrimStart('v', 'V');
                if (Version.TryParse(clean, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }
        catch (HttpRequestException e)
        {
            _logger.Debug(e, "Connection error fetching version from API {Url}.", url);
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (Exception e)
        {
            _logger.Debug(e, "Error fetching version from API {Url}.", url);
            return null;
        }
    }

    private async Task<Version?> TryFetchVersionFromLatestWebUrlAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = CreateHttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(8);

            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri != null && finalUri.AbsolutePath.Contains("/releases/tag/"))
            {
                var tag = finalUri.AbsolutePath.Substring(finalUri.AbsolutePath.LastIndexOf("/releases/tag/", StringComparison.OrdinalIgnoreCase) + "/releases/tag/".Length);
                tag = tag.Trim('/').TrimStart('v', 'V');
                if (Version.TryParse(tag, out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to fetch version from web URL {Url}", url);
            return null;
        }
    }

    private async Task<Version?> TryFetchVersionFromAtomFeedAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = CreateHttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(8);

            using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(xml) || !xml.Contains("<feed", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://www.w3.org/2005/Atom";

            var versions = doc.Descendants(ns + "entry")
                .Where(entry => !IsPrereleaseEntry(entry, ns))
                .Select(entry => (string?)entry.Element(ns + "title"))
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Select(title =>
                {
                    var clean = title!.Trim().TrimStart('v', 'V');
                    return Version.TryParse(clean, out var parsed) ? parsed : null;
                })
                .Where(v => v != null)
                .Select(v => v!)
                .ToList();

            return versions.Count > 0 ? versions.Max() : null;
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to parse version from Atom feed at {Url}", url);
            return null;
        }
    }

    private static bool IsPrereleaseEntry(XElement entry, XNamespace ns)
    {
        var title = (string?)entry.Element(ns + "title") ?? string.Empty;
        if (IsPrereleaseString(title)) return true;

        var id = (string?)entry.Element(ns + "id") ?? string.Empty;
        if (IsPrereleaseString(id)) return true;

        var link = (string?)entry.Element(ns + "link")?.Attribute("href") ?? string.Empty;
        if (IsPrereleaseString(link)) return true;

        var content = (string?)entry.Element(ns + "content") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(content))
        {
            var snippet = content.Length > 500 ? content[..500] : content;
            if (snippet.Contains("pre-release", StringComparison.OrdinalIgnoreCase) ||
                snippet.Contains("prerelease", StringComparison.OrdinalIgnoreCase) ||
                snippet.Contains("-pre", StringComparison.OrdinalIgnoreCase) ||
                snippet.Contains("-beta", StringComparison.OrdinalIgnoreCase) ||
                snippet.Contains("-alpha", StringComparison.OrdinalIgnoreCase) ||
                snippet.Contains("-rc", StringComparison.OrdinalIgnoreCase) ||
                snippet.Contains("-preview", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPrereleaseString(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("beta", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("alpha", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("rc", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("preview", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("pre-release", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("prerelease", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("test build", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("-pre", StringComparison.OrdinalIgnoreCase);
    }

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

    private void SetCheckStatus(UpdateCheckStatus status)
    {
        CheckStatus = status;
        CheckStatusChanged?.Invoke(this, status);
    }

    public void CancelAndStop()
    {
        if (_cancellationTokenSource is null || _cancellationTokenSource.IsCancellationRequested)
            return;
        var cts = _cancellationTokenSource;
        _cancellationTokenSource = null!;
        cts.Cancel();
        cts.Dispose();
        _logger.Debug("JASM update checker stopped");
    }

    private void OnNewVersionAvailable(Version e)
    {
        NewVersionAvailable?.Invoke(this, new NewVersionEventArgs(e));
    }

    public class NewVersionEventArgs : EventArgs
    {
        public Version Version { get; }

        public NewVersionEventArgs(Version version)
        {
            Version = version;
        }
    }
}

public class GitHubRelease
{
    [JsonPropertyName("target_commitish")]
    public string? TargetCommitish { get; set; }

    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; } = DateTime.MinValue;

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("assets")]
    public GitHubReleaseAsset[]? Assets { get; set; }

    [JsonConstructor]
    public GitHubRelease()
    {
    }
}

public class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonConstructor]
    public GitHubReleaseAsset()
    {
    }
}