using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GIMI_ModManager.Core.Services.Network.Models;

namespace GIMI_ModManager.Core.Contracts.Services;

public interface IMirrorRacingService
{
    IReadOnlyList<MirrorNode> DefaultGitHubMirrors { get; }

    Task<IReadOnlyList<MirrorRaceResult>> RaceAllMirrorsAsync(
        string testPathOrUrl,
        IReadOnlyList<MirrorNode>? candidateMirrors = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MirrorNode>> GetRankedMirrorsAsync(
        string testPathOrUrl,
        IReadOnlyList<MirrorNode>? candidateMirrors = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    Task<MirrorNode> GetFastestMirrorAsync(
        string testPathOrUrl,
        IReadOnlyList<MirrorNode>? candidateMirrors = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}