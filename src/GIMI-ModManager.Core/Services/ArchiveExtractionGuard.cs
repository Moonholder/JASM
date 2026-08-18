using System;
using System.IO;

namespace GIMI_ModManager.Core.Services;

/// <summary>
/// Guards archive extraction against path traversal (zip-slip) attacks.
/// </summary>
public static class ArchiveExtractionGuard
{
    /// <summary>
    /// Throws <see cref="InvalidDataException"/> when the entry path would escape <paramref name="extractPath"/>.
    /// </summary>
    public static void ValidateEntryPath(string? entryKey, string extractPath)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
            throw new InvalidDataException("Archive entry has no name.");

        var normalizedKey = entryKey.Replace('\\', '/');
        var target = Path.GetFullPath(Path.Combine(extractPath, normalizedKey.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(extractPath);
        var rootWithSeparator = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsafe archive entry path: '{entryKey}'");
    }
}