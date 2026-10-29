namespace GIMI_ModManager.Core.Services.Network.Models;

public enum ProxyMode
{
    System = 0,
    Manual = 1,
    Direct = 2
}

public enum ProxyProtocol
{
    Http = 0,
    Socks5 = 1
}

public class ProxyConfiguration
{
    public ProxyMode Mode { get; set; } = ProxyMode.System;

    public ProxyProtocol Protocol { get; set; } = ProxyProtocol.Http;

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 7890;

    public bool BypassLocal { get; set; } = true;

    public ProxyConfiguration Clone()
    {
        return new ProxyConfiguration
        {
            Mode = Mode,
            Protocol = Protocol,
            Host = Host,
            Port = Port,
            BypassLocal = BypassLocal
        };
    }
}