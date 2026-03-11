using System.Reflection;
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

    private const string ReleasesApiUrl = "https://api.github.com/repos/Moonholder/JASM/releases?per_page=2";

    public UpdateChecker(ILogger logger, ILocalSettingsService localSettingsService,
        Notifications.NotificationManager notificationManager, CancellationToken cancellationToken = default)
    {
        _logger = logger.ForContext<UpdateChecker>();
        _localSettingsService = localSettingsService;
        _notificationManager = notificationManager;
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
        await CheckForUpdatesAsync(CancellationToken.None);
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
                catch (TaskCanceledException e)
                {
                }
                catch (OperationCanceledException e)
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

        SetCheckStatus(UpdateCheckStatus.Success);

        if (CurrentVersion == latestVersion || LatestRetrievedVersion == latestVersion)
        {
            _logger.Debug("No new version available");
            return;
        }


        if (CurrentVersion < latestVersion)
        {
            if (_ignoredVersion is not null && _ignoredVersion >= latestVersion)
                return;
            LatestRetrievedVersion = latestVersion;
            OnNewVersionAvailable(latestVersion);
        }
    }

    private async Task<Version?> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        // 1. Try direct GitHub API first
        var version = await TryFetchVersionFromUrl(ReleasesApiUrl, cancellationToken);
        if (version is not null)
        {
            _logger.Debug("Got version via direct GitHub API.");
            return version;
        }

        // 2. Direct access failed, try mirror fallback (only mirrors that support API forwarding)
        _logger.Information("GitHub API direct access failed, trying mirror fallback for version check...");
        try
        {
            var mirrors = await MirrorAddressSelector.GetAvailableMirrorsAsync(cancellationToken);
            foreach (var mirror in mirrors.Where(m => !string.IsNullOrEmpty(m.Address) && m.SupportsApiForward))
            {
                if (cancellationToken.IsCancellationRequested) break;

                var mirrorUrl = mirror.Address + ReleasesApiUrl;
                version = await TryFetchVersionFromUrl(mirrorUrl, cancellationToken);
                if (version is not null)
                {
                    _logger.Information("Got version via mirror {NodeName}.", mirror.NodeName);
                    return version;
                }
            }
        }
        catch (Exception e)
        {
            _logger.Warning(e, "Mirror fallback for version check failed.");
        }

        _logger.Warning("All sources (direct + mirrors) failed to fetch version info.");
        return null;
    }

    /// <summary>
    /// Attempts to fetch the latest non-prerelease version from the specified URL.
    /// Returns null on any failure.
    /// </summary>
    private async Task<Version?> TryFetchVersionFromUrl(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = CreateHttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            var result = await httpClient.GetAsync(url, cancellationToken);
            if (!result.IsSuccessStatusCode)
            {
                _logger.Debug("Failed to get version from {Url}. Status: {StatusCode}", url, result.StatusCode);
                return null;
            }

            var text = await result.Content.ReadAsStringAsync(cancellationToken);
            var gitHubReleases =
                JsonSerializer.Deserialize<GitHubRelease[]>(text, GitHubJsonContext.Default.GitHubReleaseArray) ?? Array.Empty<GitHubRelease>();

            var latestReleases = gitHubReleases.Where(r => !r.Prerelease);
            var latestVersion = latestReleases.Select(r => new Version(r.TagName?.Trim('v') ?? "")).Max();
            return latestVersion;
        }
        catch (HttpRequestException e)
        {
            _logger.Debug(e, "Connection error fetching version from {Url}.", url);
            return null;
        }
        catch (TaskCanceledException)
        {
            // Timeout or cancellation - don't log as error
            return null;
        }
        catch (Exception e)
        {
            _logger.Debug(e, "Error fetching version from {Url}.", url);
            return null;
        }
    }

    private HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient();
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