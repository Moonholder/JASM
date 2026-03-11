using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GIMI_ModManager.WinUI.Services.AppManagement.Updating;

public static class MirrorAddressSelector
{
    private static readonly MirrorInfo[] MirrorAddresses =
    [
        new("https://gh-proxy.com/", "美国 Cloudflare CDN 1", supportsApiForward: false),
        new("https://cors.isteed.cc/", "美国 Cloudflare CDN 2", supportsApiForward: true),
        new("https://github.boki.moe/", "美国 Cloudflare CDN 3", supportsApiForward: false),
        new("https://ghproxy.net/", "英国伦敦", supportsApiForward: false),
        new("https://wget.la/", "通用节点", supportsApiForward: true),
        new("https://gh.jix.de5.net/", "Cloudflare 节点 1", supportsApiForward: true),
        new("https://dl.jix.de5.net/", "Cloudflare 节点 2", supportsApiForward: true),
    ];

    private const string TestUrl = "https://raw.githubusercontent.com/Moonholder/JASM/main/README.md";

    /// <summary>
    /// Tests all mirrors concurrently and returns available ones sorted by latency (fastest first).
    /// Always appends a "GitHub Direct" fallback entry at the end.
    /// </summary>
    public static async Task<List<MirrorInfo>> GetAvailableMirrorsAsync(CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(6));

        var tasks = MirrorAddresses.Select(m => TestMirrorAsync(m, cts.Token)).ToArray();

        try
        {
            var results = await Task.WhenAll(tasks);

            var available = results
                .Where(r => r.IsAvailable)
                .OrderBy(r => r.LatencyMs)
                .Select(r => r.Mirror)
                .ToList();

            // Always add GitHub Direct as ultimate fallback
            available.Add(new MirrorInfo("", "GitHub Direct"));
            return available;
        }
        catch (OperationCanceledException)
        {
            // If timed out, collect whatever completed successfully
            var available = tasks
                .Where(t => t.IsCompletedSuccessfully && t.Result.IsAvailable)
                .Select(t => t.Result)
                .OrderBy(r => r.LatencyMs)
                .Select(r => r.Mirror)
                .ToList();

            available.Add(new MirrorInfo("", "GitHub Direct"));
            return available;
        }
    }

    private static async Task<MirrorTestResult> TestMirrorAsync(MirrorInfo mirror, CancellationToken token)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var sw = Stopwatch.StartNew();
            using var response = await httpClient.GetAsync(mirror.Address + TestUrl, HttpCompletionOption.ResponseHeadersRead, token);
            sw.Stop();

            return new MirrorTestResult(mirror, response.IsSuccessStatusCode, sw.Elapsed.TotalMilliseconds);
        }
        catch
        {
            return new MirrorTestResult(mirror, false, double.MaxValue);
        }
    }

    public class MirrorInfo(string address, string nodeName, bool supportsApiForward = true)
    {
        public string Address { get; } = address;
        public string NodeName { get; } = nodeName;
        public bool SupportsApiForward { get; } = supportsApiForward;
    }

    public record MirrorTestResult(MirrorInfo Mirror, bool IsAvailable, double LatencyMs);
}