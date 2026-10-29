using System;
using System.Threading;
using System.Threading.Tasks;
using GIMI_ModManager.Core.Services.Network.Models;

namespace GIMI_ModManager.Core.Contracts.Services;

public interface IChunkedDownloadEngine
{
    Task DownloadAsync(
        string url,
        string destinationFilePath,
        IProgress<DownloadProgressInfo>? progress = null,
        ChunkedDownloadOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<RangeProbeResult> ProbeRangeSupportAsync(
        string url,
        CancellationToken cancellationToken = default);
}