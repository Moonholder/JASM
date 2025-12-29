using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Helpers;
using System.Text.RegularExpressions;

namespace GIMI_ModManager.Core.Entities.Mods.Helpers;

public static class SkinModHelpers
{
    /// <summary>
    /// 将 URI 路径转换为相对于 Mod 根目录的路径
    /// </summary>
    public static string? UriPathToModRelativePath(ISkinMod mod, string? uriPath)
    {
        if (string.IsNullOrWhiteSpace(uriPath))
            return null;

        try
        {
            string fullPathToCheck;
            if (Uri.TryCreate(uriPath, UriKind.Absolute, out var uriResult) && uriResult.Scheme == Uri.UriSchemeFile)
            {
                fullPathToCheck = uriResult.LocalPath;
            }
            else
            {
                fullPathToCheck = uriPath;
            }

            var relativePath = Path.GetRelativePath(mod.FullPath, fullPathToCheck);

            if (relativePath.StartsWith("..") || Path.IsPathRooted(relativePath))
            {
                var filename = Path.GetFileName(fullPathToCheck);
                return string.IsNullOrWhiteSpace(filename) ? null : filename;
            }

            return relativePath;
        }
        catch
        {
            var filename = Path.GetFileName(uriPath);
            return string.IsNullOrWhiteSpace(filename) ? null : filename;
        }
    }

    public static Uri? RelativeModPathToAbsPath(string modPath, string? relativeModPath)
    {
        if (string.IsNullOrWhiteSpace(relativeModPath))
            return null;

        var fullPath = Path.GetFullPath(Path.Combine(modPath, relativeModPath));

        return new Uri(fullPath);
    }

    public static bool IsInModFolder(ISkinMod mod, Uri path)
    {
        if (path.Scheme != Uri.UriSchemeFile)
            return false;

        var modFullPath = Path.GetFullPath(mod.FullPath).TrimEnd(Path.DirectorySeparatorChar);
        var targetFullPath = Path.GetFullPath(path.LocalPath);

        return targetFullPath.StartsWith(modFullPath, StringComparison.OrdinalIgnoreCase);
    }

    public static Uri? StringUrlToUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return Uri.TryCreate(url, UriKind.Absolute, out var result) ? result : null;
    }

    public static Guid StringToGuid(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid))
            return Guid.NewGuid();

        return Guid.TryParse(guid, out var result) ? result : Guid.NewGuid();
    }


    public static readonly string[] _imageNamePriority = [".jasm_cover", "preview", "cover"];

    private static readonly HashSet<string> _supportedExtensions = Constants.SupportedImageExtensions
        .Select(e => e.ToLowerInvariant())
        .ToHashSet();

    public static Uri[] DetectModPreviewImages(string modDirPath)
    {
        var modDir = new DirectoryInfo(modDirPath);
        if (!modDir.Exists)
            return [];

        var images = modDir.EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => _supportedExtensions.Contains(file.Extension.ToLowerInvariant()))
            .Select(file => new
            {
                FileInfo = file,
                Priority = GetImagePriority(file.Name)
            })

            .OrderBy(x => x.Priority)
            .ThenBy(x => x.FileInfo.Name)
            .Select(x => new Uri(x.FileInfo.FullName))
            .ToArray();

        return images;
    }

    private static int GetImagePriority(string fileName)
    {
        for (int i = 0; i < _imageNamePriority.Length; i++)
        {
            if (fileName.StartsWith(_imageNamePriority[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return int.MaxValue;
    }
}