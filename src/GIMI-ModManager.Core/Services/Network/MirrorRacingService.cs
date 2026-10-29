using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Services.Network.Models;
using Serilog;

namespace GIMI_ModManager.Core.Services.Network;

public sealed class MirrorRacingService : IMirrorRacingService, IDisposable
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ConcurrentDictionary<string, (int Failures, DateTime CooldownUntilUtc)> _nodeHealth = new();

    public static readonly MirrorNode DirectMirror = new("", "GitHub Direct", SupportsApiForward: false);

    public IReadOnlyList<MirrorNode> DefaultGitHubMirrors { get; } =
    [
        DirectMirror,
        new("https://gh-proxy.com/", "Cloudflare CDN 1", SupportsApiForward: false),
        new("https://ghfast.top/", "Fast CDN", SupportsApiForward: false),
        new("https://ghproxy.net/", "London Node", SupportsApiForward: false),
        new("https://wget.la/", "General Node", SupportsApiForward: true),
    ];

    public MirrorRacingService(ILogger logger, DynamicAppWebProxy? dynamicProxy = null, HttpClient? httpClient = null)
    {
        _logger = logger?.ForContext<MirrorRacingService>() ?? throw new ArgumentNullException(nameof(logger));

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5,
                ConnectTimeout = TimeSpan.FromSeconds(5)
            };

            if (dynamicProxy != null)
            {
                handler.Proxy = dynamicProxy;
                handler.UseProxy = true;
            }

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "JASM-Mirror-Prober");
            _ownsHttpClient = true;
        }
    }

    public async Task<IReadOnlyList<MirrorRaceResult>> RaceAllMirrorsAsync(
        string testPathOrUrl,
        IReadOnlyList<MirrorNode>? candidateMirrors = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testPathOrUrl);

        var mirrors = candidateMirrors ?? DefaultGitHubMirrors;
        var probeTimeout = timeout ?? TimeSpan.FromSeconds(5);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(probeTimeout);

        var probeTasks = mirrors.Select(m => ProbeSingleMirrorAsync(m, testPathOrUrl, linkedCts.Token)).ToArray();

        MirrorRaceResult[] results;
        try
        {
            results = await Task.WhenAll(probeTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Gather any probe that finished successfully before timeout
            results = probeTasks
                .Where(t => t.IsCompletedSuccessfully)
                .Select(t => t.Result)
                .ToArray();
        }

        var resultMap = results.ToDictionary(r => r.Mirror.Address);
        var completeResults = new List<MirrorRaceResult>(mirrors.Count);

        foreach (var mirror in mirrors)
        {
            if (resultMap.TryGetValue(mirror.Address, out var res))
            {
                completeResults.Add(res);
            }
            else
            {
                completeResults.Add(new MirrorRaceResult(mirror, false, double.MaxValue, "Probe timed out."));
            }
        }

        return completeResults;
    }

    public async Task<IReadOnlyList<MirrorNode>> GetRankedMirrorsAsync(
        string testPathOrUrl,
        IReadOnlyList<MirrorNode>? candidateMirrors = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var raceResults = await RaceAllMirrorsAsync(testPathOrUrl, candidateMirrors, timeout, cancellationToken).ConfigureAwait(false);

        var availableMirrors = raceResults
            .Where(r => r.IsAvailable)
            .OrderBy(r => r.LatencyMs)
            .Select(r => r.Mirror)
            .ToList();

        // Always guarantee the Direct mirror exists as a safe fallback
        if (!availableMirrors.Any(m => string.IsNullOrEmpty(m.Address)))
        {
            availableMirrors.Add(DirectMirror);
        }

        return availableMirrors;
    }

    public async Task<MirrorNode> GetFastestMirrorAsync(
        string testPathOrUrl,
        IReadOnlyList<MirrorNode>? candidateMirrors = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var ranked = await GetRankedMirrorsAsync(testPathOrUrl, candidateMirrors, timeout, cancellationToken).ConfigureAwait(false);
        return ranked.Count > 0 ? ranked[0] : DirectMirror;
    }

    private async Task<MirrorRaceResult> ProbeSingleMirrorAsync(MirrorNode mirror, string testPathOrUrl, CancellationToken cancellationToken)
    {
        var mirrorKey = mirror.Address;

        if (_nodeHealth.TryGetValue(mirrorKey, out var health))
        {
            if (health.Failures >= 2 && DateTime.UtcNow < health.CooldownUntilUtc)
            {
                return new MirrorRaceResult(mirror, false, double.MaxValue, "Temporarily in cooldown due to previous failures.");
            }
        }

        var fullUrl = ResolveProbeUrl(mirror, testPathOrUrl);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            var latency = stopwatch.Elapsed.TotalMilliseconds;

            if (response.IsSuccessStatusCode)
            {
                _nodeHealth.TryRemove(mirrorKey, out _);
                return new MirrorRaceResult(mirror, true, latency);
            }

            RecordFailure(mirrorKey);
            return new MirrorRaceResult(mirror, false, latency, $"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new MirrorRaceResult(mirror, false, stopwatch.Elapsed.TotalMilliseconds, "Request cancelled or timed out.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            RecordFailure(mirrorKey);
            return new MirrorRaceResult(mirror, false, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
        }
    }

    private static string ResolveProbeUrl(MirrorNode mirror, string testPathOrUrl)
    {
        if (string.IsNullOrEmpty(mirror.Address))
        {
            return testPathOrUrl;
        }

        var baseAddress = mirror.Address.TrimEnd('/');
        if (testPathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            testPathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return $"{baseAddress}/{testPathOrUrl}";
        }

        return $"{baseAddress}/{testPathOrUrl.TrimStart('/')}";
    }

    private void RecordFailure(string mirrorKey)
    {
        _nodeHealth.AddOrUpdate(
            mirrorKey,
            _ => (1, DateTime.UtcNow.AddMinutes(5)),
            (_, current) => (current.Failures + 1, DateTime.UtcNow.AddMinutes(Math.Min(30, 5 * (current.Failures + 1)))));
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}