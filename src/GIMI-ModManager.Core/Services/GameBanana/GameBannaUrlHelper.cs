using System.Diagnostics.CodeAnalysis;
using GIMI_ModManager.Core.Services.GameBanana.Models;

namespace GIMI_ModManager.Core.Services.GameBanana;

public static class GameBananaUrlHelper
{
    /// <summary>
    /// Known GameBanana model names that correspond to URL path segments (lowercase).
    /// Maps lowercase segment to proper API model name.
    /// </summary>
    private static readonly Dictionary<string, string> KnownModelNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mods"] = "Mod",
        ["wips"] = "Wip",
        ["tools"] = "Tool",
        ["requests"] = "Request",
        ["questions"] = "Question",
        ["tuts"] = "Tut",
    };

    /// <summary>
    /// Model names that do NOT have downloadable files.
    /// </summary>
    private static readonly HashSet<string> DownloadableModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Mod",
        "Wip",
        "Tool",
    };

    public static bool TryGetModIdFromUrl(Uri url, [NotNullWhen(true)] out GbModId? modId)
    {
        return TryGetModIdFromUrl(url, out modId, out _);
    }

    public static bool TryGetModIdFromUrl(Uri url, [NotNullWhen(true)] out GbModId? modId, out string modelName)
    {
        modId = null;
        modelName = "Mod";

        if (url.Host != "gamebanana.com" || url.Scheme != Uri.UriSchemeHttps)
            return false;

        var segments = url.Segments;

        if (segments.Length < 3) // e.g. /mods/12345 => ["/", "mods/", "12345"]
            return false;

        // Extract model name from path segment (e.g. "mods/" => "mods" => "Mod")
        var modelSegment = segments[^2].TrimEnd('/');
        if (KnownModelNames.TryGetValue(modelSegment, out var mappedName))
        {
            modelName = mappedName;
        }
        else
        {
            // Fallback: capitalize first letter (e.g. "sounds" => "Sound")
            // Remove trailing 's' for plural => singular
            var singular = modelSegment.EndsWith('s') ? modelSegment[..^1] : modelSegment;
            modelName = char.ToUpperInvariant(singular[0]) + singular[1..].ToLowerInvariant();
        }

        modId = new GbModId(segments.Last().TrimEnd('/'));

        if (modId.ModId.Contains('/'))
            return false;

        return true;
    }

    /// <summary>
    /// Returns true if the model name supports downloadable files.
    /// Request and Question types do not have files.
    /// </summary>
    public static bool HasDownloadableFiles(string modelName)
    {
        return DownloadableModels.Contains(modelName);
    }
}