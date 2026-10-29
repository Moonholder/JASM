using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GIMI_ModManager.Core.Services.Network;
using GIMI_ModManager.Core.Services.Network.Models;
using Serilog;
using Xunit;

namespace JASM.Tests;

public class ChunkedDownloadEngineTests : IDisposable
{
    private readonly string _testTempDir;

    public ChunkedDownloadEngineTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), "JASM_ChunkedTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testTempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testTempDir))
            {
                Directory.Delete(_testTempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup failure in test teardown
        }
    }

    private sealed class MockChunkHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        private readonly bool _supportsRange;

        private readonly object _lock = new();

        public List<string> RequestedRanges { get; } = [];

        public MockChunkHttpMessageHandler(byte[] payload, bool supportsRange = true)
        {
            _payload = payload;
            _supportsRange = supportsRange;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var range = request.Headers.Range;

            if (!_supportsRange || range == null || range.Ranges.Count == 0)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_payload)
                };
                response.Content.Headers.ContentLength = _payload.Length;
                return Task.FromResult(response);
            }

            var requestedRange = range.Ranges.First();
            long from = requestedRange.From ?? 0;
            long to = requestedRange.To ?? (_payload.Length - 1);

            lock (_lock)
            {
                RequestedRanges.Add($"{from}-{to}");
            }

            long sliceLength = Math.Max(0, to - from + 1);
            var slice = new byte[sliceLength];
            Array.Copy(_payload, from, slice, 0, sliceLength);

            var partialResponse = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(slice)
            };
            partialResponse.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, _payload.Length);
            partialResponse.Content.Headers.ContentLength = sliceLength;

            return Task.FromResult(partialResponse);
        }
    }

    [Fact]
    public async Task ProbeRangeSupport_ReturnsPartialContent_WhenRangeSupported()
    {
        var testData = new byte[1024 * 1024]; // 1 MB
        var handler = new MockChunkHttpMessageHandler(testData, supportsRange: true);
        using var httpClient = new HttpClient(handler);
        var logger = new LoggerConfiguration().CreateLogger();
        using var engine = new ChunkedDownloadEngine(logger, httpClient: httpClient);

        var probe = await engine.ProbeRangeSupportAsync("https://example.com/test.bin");

        Assert.True(probe.SupportsRange);
        Assert.Equal(testData.Length, probe.ContentLength);
    }

    [Fact]
    public async Task ProbeRangeSupport_ReturnsFalse_When200Returned()
    {
        var testData = new byte[1024 * 1024];
        var handler = new MockChunkHttpMessageHandler(testData, supportsRange: false);
        using var httpClient = new HttpClient(handler);
        var logger = new LoggerConfiguration().CreateLogger();
        using var engine = new ChunkedDownloadEngine(logger, httpClient: httpClient);

        var probe = await engine.ProbeRangeSupportAsync("https://example.com/test.bin");

        Assert.False(probe.SupportsRange);
        Assert.Equal(testData.Length, probe.ContentLength);
    }

    [Fact]
    public async Task DownloadAsync_SingleStreamFallback_WhenRangeNotSupported()
    {
        var testData = new byte[1024 * 512]; // 512 KB
        new Random(42).NextBytes(testData);

        var handler = new MockChunkHttpMessageHandler(testData, supportsRange: false);
        using var httpClient = new HttpClient(handler);
        var logger = new LoggerConfiguration().CreateLogger();
        using var engine = new ChunkedDownloadEngine(logger, httpClient: httpClient);

        var targetFile = Path.Combine(_testTempDir, "single_stream.bin");

        await engine.DownloadAsync("https://example.com/file.bin", targetFile);

        Assert.True(File.Exists(targetFile));
        var downloadedBytes = await File.ReadAllBytesAsync(targetFile);
        Assert.Equal(testData, downloadedBytes);
    }

    [Fact]
    public async Task DownloadAsync_MultiChunkParallel_CombinesAndSavesValidFile()
    {
        // 6 MB test payload
        int size = 6 * 1024 * 1024;
        var testData = new byte[size];
        new Random(12345).NextBytes(testData);

        var handler = new MockChunkHttpMessageHandler(testData, supportsRange: true);
        using var httpClient = new HttpClient(handler);
        var logger = new LoggerConfiguration().CreateLogger();
        using var engine = new ChunkedDownloadEngine(logger, httpClient: httpClient);

        var targetFile = Path.Combine(_testTempDir, "multi_chunk.bin");
        var options = new ChunkedDownloadOptions
        {
            Concurrency = 4,
            MinChunkSize = 1024 * 1024,
            MinFileSizeForChunking = 2 * 1024 * 1024
        };

        var progressReports = new List<DownloadProgressInfo>();
        var progress = new Progress<DownloadProgressInfo>(progressReports.Add);

        await engine.DownloadAsync("https://example.com/multi_chunk.bin", targetFile, progress, options);

        Assert.True(File.Exists(targetFile));
        Assert.False(File.Exists(targetFile + ".part"));
        Assert.False(File.Exists(targetFile + ".part.meta"));

        var downloadedBytes = await File.ReadAllBytesAsync(targetFile);
        Assert.Equal(testData.Length, downloadedBytes.Length);
        Assert.Equal(testData, downloadedBytes);

        // Verify multiple chunks were requested
        Assert.True(handler.RequestedRanges.Count >= 4);
    }

    [Fact]
    public async Task DownloadAsync_ResumesInterruptedSession_WhenMetaPresent()
    {
        int size = 4 * 1024 * 1024; // 4 MB
        var testData = new byte[size];
        new Random(999).NextBytes(testData);

        var targetFile = Path.Combine(_testTempDir, "resumable.bin");
        var partPath = targetFile + ".part";
        var metaPath = targetFile + ".part.meta";
        var url = "https://example.com/resumable.bin";

        // Manually simulate an interrupted session where Chunk 0 (0..2097151) is already complete
        var chunk0Length = 2 * 1024 * 1024;
        var meta = new DownloadSessionMetadata
        {
            Url = url,
            TotalBytes = size,
            Chunks =
            [
                new() { Index = 0, StartOffset = 0, EndOffset = chunk0Length - 1, BytesDownloaded = chunk0Length },
                new() { Index = 1, StartOffset = chunk0Length, EndOffset = size - 1, BytesDownloaded = 0 }
            ]
        };

        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta));

        // Create sparse part file and write chunk 0 data
        using (var fs = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            fs.SetLength(size);
            fs.Write(testData, 0, chunk0Length);
        }

        var handler = new MockChunkHttpMessageHandler(testData, supportsRange: true);
        using var httpClient = new HttpClient(handler);
        var logger = new LoggerConfiguration().CreateLogger();
        using var engine = new ChunkedDownloadEngine(logger, httpClient: httpClient);

        var options = new ChunkedDownloadOptions
        {
            Concurrency = 2,
            MinChunkSize = 1024 * 1024,
            MinFileSizeForChunking = 2 * 1024 * 1024
        };

        await engine.DownloadAsync(url, targetFile, null, options);

        Assert.True(File.Exists(targetFile));
        var downloadedBytes = await File.ReadAllBytesAsync(targetFile);
        Assert.Equal(testData, downloadedBytes);

        // Verify that Chunk 0 (0 to chunk0Length - 1) was NOT requested again (except probe 0-0)
        var chunk0Range = $"0-{chunk0Length - 1}";
        Assert.DoesNotContain(chunk0Range, handler.RequestedRanges);

        // Verify Chunk 1 was requested
        var chunk1Range = $"{chunk0Length}-{size - 1}";
        Assert.Contains(chunk1Range, handler.RequestedRanges);
    }
}