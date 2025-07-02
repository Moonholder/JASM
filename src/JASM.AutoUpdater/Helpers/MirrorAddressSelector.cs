using System;
using System.Collections.Generic;
using System.Linq;

public static class MirrorAddressSelector
{
    private static readonly List<MirrorInfo> mirrorAddresses =
        [
        new MirrorInfo("https://ghfast.top/", "韩国"),
        new MirrorInfo("https://gh-proxy.com/", "美国4 Cloudflare CDN"),
        new MirrorInfo("https://cors.isteed.cc/", "美国5 Cloudflare CDN"),
        new MirrorInfo("https://ghproxy.cfd/", "美国8 洛杉矶"),
        new MirrorInfo("https://github.boki.moe/", "美国9 Cloudflare CDN"),
        new MirrorInfo("https://ghproxy.net/", "英国伦敦"),
        new MirrorInfo("https://wget.la/", "其他"),
        ];

    private static readonly List<MirrorInfo> usedAddresses = [];

    private static readonly Random random = new();

    public static MirrorInfo GetNextMirror()
    {
        if (usedAddresses.Count == mirrorAddresses.Count)
        {
            usedAddresses.Clear();
        }

        var availableAddresses = mirrorAddresses.Except(usedAddresses).ToList();
        int index = random.Next(0, availableAddresses.Count);
        var selectedAddress = availableAddresses[index];
        usedAddresses.Add(selectedAddress);

        return selectedAddress;
    }

    public class MirrorInfo(string address, string nodeName)
    {
        public string Address { get; set; } = address;
        public string NodeName { get; set; } = nodeName;
    }
}