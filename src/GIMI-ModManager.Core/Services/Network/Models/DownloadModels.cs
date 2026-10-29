using System;
using System.Collections.Generic;
using System.Net;

namespace GIMI_ModManager.Core.Services.Network.Models;

public record DownloadProgressInfo(
    long BytesReceived,
    long? TotalBytes,
    double BytesPerSecond,
    int? Percentage,
    int ActiveConnections = 1);

public record ChunkedDownloadOptions
{
    public int Concurrency { get; init; } = 4;

    public long MinFileSizeForChunking { get; init; } = 15 * 1024 * 1024; // 15 MB

    public long MinChunkSize { get; init; } = 5 * 1024 * 1024; // 5 MB

    public int MaxRetriesPerChunk { get; init; } = 3;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan SpeedEvaluationInterval { get; init; } = TimeSpan.FromSeconds(1);
}

public record ChunkMetadata
{
    public int Index { get; init; }

    public long StartOffset { get; init; }

    public long EndOffset { get; init; }

    public long BytesDownloaded { get; set; }

    public long ExpectedLength => EndOffset - StartOffset + 1;

    public bool IsCompleted => BytesDownloaded >= ExpectedLength;
}

public class DownloadSessionMetadata
{
    public string Url { get; set; } = string.Empty;

    public long TotalBytes { get; set; }

    public List<ChunkMetadata> Chunks { get; set; } = [];
}

public record RangeProbeResult(
    bool SupportsRange,
    long? ContentLength,
    HttpStatusCode StatusCode,
    string? ResolvedUrl = null);