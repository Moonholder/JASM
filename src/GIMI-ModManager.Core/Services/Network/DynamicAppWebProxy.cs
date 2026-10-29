using System;
using System.Net;
using GIMI_ModManager.Core.Contracts.Services;

namespace GIMI_ModManager.Core.Services.Network;

public sealed class DynamicAppWebProxy : IWebProxy
{
    private readonly IProxySettingsService _proxySettingsService;

    public DynamicAppWebProxy(IProxySettingsService proxySettingsService)
    {
        _proxySettingsService = proxySettingsService ?? throw new ArgumentNullException(nameof(proxySettingsService));
    }

    public ICredentials? Credentials
    {
        get => _proxySettingsService.GetUnderlyingProxy()?.Credentials;
        set { }
    }

    public Uri? GetProxy(Uri destination)
    {
        var proxy = _proxySettingsService.GetUnderlyingProxy();
        return proxy != null ? proxy.GetProxy(destination) : destination;
    }

    public bool IsBypassed(Uri host)
    {
        var proxy = _proxySettingsService.GetUnderlyingProxy();
        return proxy == null || proxy.IsBypassed(host);
    }
}