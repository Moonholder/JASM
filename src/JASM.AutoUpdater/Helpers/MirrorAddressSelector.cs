using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

public static class MirrorAddressSelector
{
    private static readonly List<MirrorInfo> mirrorAddresses =
    [
        new MirrorInfo("https://gh-proxy.com/", "美国4 Cloudflare CDN"),
        new MirrorInfo("https://cors.isteed.cc/", "美国5 Cloudflare CDN"),
        new MirrorInfo("https://github.boki.moe/", "美国9 Cloudflare CDN"),
        new MirrorInfo("https://ghproxy.net/", "英国伦敦"),
        new MirrorInfo("https://wget.la/", "其他"),
    ];

    private static readonly List<MirrorInfo> usedAddresses = [];
    private static readonly Random random = new();
    private static readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public static async Task<MirrorInfo> GetBestMirrorAsync()
    {
        var tasks = mirrorAddresses.Select(TestMirrorAsync).ToList();
        var results = await Task.WhenAll(tasks);

        var availableMirrors = results
            .Where(r => r.IsAvailable)
            .OrderBy(r => r.Latency)
            .ToList();

        return availableMirrors.FirstOrDefault()?.Mirror ?? GetNextMirror();
    }

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

    public static async Task<MirrorTestResult> TestMirrorAsync(MirrorInfo mirror)
    {
        try
        {
            var startTime = DateTime.Now;
            var response = await httpClient.GetAsync(mirror.Address + "https://raw.githubusercontent.com/Moonholder/JASM/main/README.md");
            var Latency = mirror.Latency = (DateTime.Now - startTime).TotalMilliseconds;
            var IsAvailable = mirror.IsAvailable = response.IsSuccessStatusCode;

            return new MirrorTestResult(
                mirror,
                IsAvailable,
                Latency
            );
        }
        catch
        {
            return new MirrorTestResult(mirror, false, double.MaxValue);
        }
    }

    public class MirrorInfo(string address, string nodeName)
    {
        public string Address { get; set; } = address;
        public string NodeName { get; set; } = nodeName;
        public double Latency { get; set; }
        public bool IsAvailable { get; set; }
    }

    public record MirrorTestResult(MirrorInfo Mirror, bool IsAvailable, double Latency);
}