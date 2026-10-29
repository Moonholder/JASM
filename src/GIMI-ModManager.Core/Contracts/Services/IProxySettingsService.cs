using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using GIMI_ModManager.Core.Services.Network.Models;

namespace GIMI_ModManager.Core.Contracts.Services;

public record ProxyTestResult(bool IsSuccess, double LatencyMs, string? ErrorMessage = null);

public interface IProxySettingsService
{
    ProxyConfiguration CurrentConfiguration { get; }

    Task<ProxyConfiguration> GetConfigurationAsync();

    Task SaveConfigurationAsync(ProxyConfiguration configuration);

    IWebProxy? GetUnderlyingProxy();

    event EventHandler<ProxyConfiguration>? ConfigurationChanged;

    Task<ProxyTestResult> TestProxyConnectivityAsync(ProxyConfiguration configuration, CancellationToken cancellationToken = default);
}