namespace GIMI_ModManager.Core.Services.Network.Models;

public record MirrorNode(string Address, string NodeName, bool SupportsApiForward = true);

public record MirrorRaceResult(
    MirrorNode Mirror,
    bool IsAvailable,
    double LatencyMs,
    string? ErrorMessage = null);