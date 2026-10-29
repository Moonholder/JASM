using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Services.Network.Models;
using GIMI_ModManager.WinUI.Contracts.Services;
using Serilog;

namespace GIMI_ModManager.WinUI.Services.Network;

public sealed class ProxySettingsService : IProxySettingsService
{
    private const string SettingKey = "Network_ProxyConfiguration";
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private ProxyConfiguration _currentConfiguration = new();
    private IWebProxy? _cachedUnderlyingProxy;
    private bool _isLoaded;

    public event EventHandler<ProxyConfiguration>? ConfigurationChanged;

    public ProxyConfiguration CurrentConfiguration
    {
        get
        {
            lock (_lock)
            {
                return _currentConfiguration.Clone();
            }
        }
    }

    public ProxySettingsService(ILocalSettingsService localSettingsService, ILogger logger)
    {
        _localSettingsService = localSettingsService ?? throw new ArgumentNullException(nameof(localSettingsService));
        _logger = logger?.ForContext<ProxySettingsService>() ?? throw new ArgumentNullException(nameof(logger));
        _cachedUnderlyingProxy = HttpClient.DefaultProxy;
    }

    public async Task<ProxyConfiguration> GetConfigurationAsync()
    {
        if (_isLoaded)
        {
            return CurrentConfiguration;
        }

        await _loadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isLoaded)
            {
                return CurrentConfiguration;
            }

            var stored = await _localSettingsService.ReadSettingAsync<ProxyConfiguration>(SettingKey, SettingScope.App).ConfigureAwait(false);
            lock (_lock)
            {
                if (stored != null)
                {
                    _currentConfiguration = stored;
                }
                UpdateCachedProxy_NoLock();
                _isLoaded = true;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load proxy settings, using default system proxy.");
            lock (_lock)
            {
                _currentConfiguration = new ProxyConfiguration();
                UpdateCachedProxy_NoLock();
                _isLoaded = true;
            }
        }
        finally
        {
            _loadLock.Release();
        }

        return CurrentConfiguration;
    }

    public async Task SaveConfigurationAsync(ProxyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        await _loadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_lock)
            {
                _currentConfiguration = configuration.Clone();
                UpdateCachedProxy_NoLock();
                _isLoaded = true;
            }

            await _localSettingsService.SaveSettingAsync(SettingKey, _currentConfiguration, SettingScope.App).ConfigureAwait(false);
            _logger.Information("Proxy configuration saved: Mode={Mode}, Protocol={Protocol}, Host={Host}, Port={Port}",
                _currentConfiguration.Mode, _currentConfiguration.Protocol, _currentConfiguration.Host, _currentConfiguration.Port);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to persist proxy settings to LocalSettingsService.");
        }
        finally
        {
            _loadLock.Release();
        }

        ConfigurationChanged?.Invoke(this, CurrentConfiguration);
    }

    public IWebProxy? GetUnderlyingProxy()
    {
        lock (_lock)
        {
            return _cachedUnderlyingProxy;
        }
    }

    private void UpdateCachedProxy_NoLock()
    {
        switch (_currentConfiguration.Mode)
        {
            case ProxyMode.Direct:
                _cachedUnderlyingProxy = null;
                break;

            case ProxyMode.Manual:
                try
                {
                    var scheme = _currentConfiguration.Protocol == ProxyProtocol.Socks5 ? "socks5" : "http";
                    var uri = new Uri($"{scheme}://{_currentConfiguration.Host}:{_currentConfiguration.Port}");
                    _cachedUnderlyingProxy = new WebProxy(uri)
                    {
                        BypassProxyOnLocal = _currentConfiguration.BypassLocal
                    };
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Invalid manual proxy configuration, fallback to system proxy.");
                    _cachedUnderlyingProxy = HttpClient.DefaultProxy;
                }
                break;

            case ProxyMode.System:
            default:
                _cachedUnderlyingProxy = HttpClient.DefaultProxy;
                break;
        }
    }

    public async Task<ProxyTestResult> TestProxyConnectivityAsync(ProxyConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(6));

        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(5)
        };

        switch (configuration.Mode)
        {
            case ProxyMode.Direct:
                handler.UseProxy = false;
                break;
            case ProxyMode.Manual:
                try
                {
                    var scheme = configuration.Protocol == ProxyProtocol.Socks5 ? "socks5" : "http";
                    var uri = new Uri($"{scheme}://{configuration.Host}:{configuration.Port}");
                    handler.Proxy = new WebProxy(uri)
                    {
                        BypassProxyOnLocal = configuration.BypassLocal
                    };
                    handler.UseProxy = true;
                }
                catch (Exception ex)
                {
                    return new ProxyTestResult(false, 0, $"Invalid proxy URI: {ex.Message}");
                }
                break;
            case ProxyMode.System:
            default:
                handler.Proxy = HttpClient.DefaultProxy;
                handler.UseProxy = true;
                break;
        }

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(6)
        };
        client.DefaultRequestHeaders.Add("User-Agent", "JASM-Connectivity-Probe");

        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await client.GetAsync("https://api.github.com/zen", HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            sw.Stop();

            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new ProxyTestResult(true, sw.Elapsed.TotalMilliseconds);
            }

            return new ProxyTestResult(false, sw.Elapsed.TotalMilliseconds, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return new ProxyTestResult(false, sw.Elapsed.TotalMilliseconds, "Connection timed out after 5 seconds.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ProxyTestResult(false, sw.Elapsed.TotalMilliseconds, ex.Message);
        }
    }
}