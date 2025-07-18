﻿using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Helpers;
using System.Text.RegularExpressions;

namespace GIMI_ModManager.Core.Entities.Mods.Helpers;

public static class SkinModHelpers
{
    public static string? UriPathToModRelativePath(ISkinMod mod, string? uriPath)
    {
        if (string.IsNullOrWhiteSpace(uriPath))
            return null;

        var modPath = mod.FullPath;

        var modUri = Uri.TryCreate(modPath, UriKind.Absolute, out var result) &&
                     result.Scheme == Uri.UriSchemeFile
            ? result
            : null;

        var uri = Uri.TryCreate(uriPath, UriKind.Absolute, out var uriResult) &&
                  uriResult.Scheme == Uri.UriSchemeFile
            ? uriResult
            : null;

        // This is technically the only path that should be used.
        if (modUri is not null && uri is not null)
        {
            var relativeUri = modUri.MakeRelativeUri(uri);

            var modName = Uri.UnescapeDataString(modUri.Segments.LastOrDefault(string.Empty));
            if (string.IsNullOrWhiteSpace(modName))
                modName = mod.Name;

            // var relativePath = relativeUri.OriginalString.Replace($"{modName}/", "");
            var regex = new Regex(Regex.Escape($"{modName}/"));
            var decodedRelativeUri = Uri.UnescapeDataString(relativeUri.OriginalString);
            var relativePath = regex.Replace(decodedRelativeUri, "", 1);
            return relativePath;
        }


        if (Uri.IsWellFormedUriString(uriPath, UriKind.Absolute))
        {
            var filename = Path.GetFileName(uriPath);
            return string.IsNullOrWhiteSpace(filename) ? null : filename;
        }

        var absPath = Path.GetFileName(uriPath);

        var file = Path.GetFileName(absPath);
        return string.IsNullOrWhiteSpace(file) ? null : file;
    }

    public static Uri? RelativeModPathToAbsPath(string modPath, string? relativeModPath)
    {
        if (string.IsNullOrWhiteSpace(relativeModPath))
            return null;

        var uri = Uri.TryCreate(Path.Combine(modPath, relativeModPath), UriKind.Absolute, out var result) &&
                  result.Scheme == Uri.UriSchemeFile
            ? result
            : null;

        return uri;
    }

    public static bool IsInModFolder(ISkinMod mod, Uri path)
    {
        if (path.Scheme != Uri.UriSchemeFile)
            return false;

        var fsPath = path.LocalPath;


        return fsPath.StartsWith(mod.FullPath, StringComparison.OrdinalIgnoreCase);
    }


    public static Uri? StringUrlToUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        return Uri.IsWellFormedUriString(url, UriKind.Absolute) ? new Uri(url) : null;
    }

    public static Guid StringToGuid(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid))
            return Guid.NewGuid();

        return Guid.TryParse(guid, out var result) ? result : Guid.NewGuid();
    }

    public static readonly string[] _imageNamePriority = [".jasm_cover", "preview", "cover"];

    public static Uri[] DetectModPreviewImages(string modDirPath)
    {
        var modDir = new DirectoryInfo(modDirPath);
        if (!modDir.Exists)
            return [];

        var supportedExtensions = Constants.SupportedImageExtensions
            .Select(e => e.ToLowerInvariant())
            .ToHashSet();

        var images = modDir.EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => _imageNamePriority.Any(prefix =>
                file.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Where(file => supportedExtensions.Contains(file.Extension.ToLowerInvariant()))
            .ToList();

        // Sort images by priority
        foreach (var imageName in _imageNamePriority.Reverse())
        {
            var image = images.FirstOrDefault(x => x.Name.StartsWith(imageName, StringComparison.CurrentCultureIgnoreCase));
            if (image is null)
                continue;

            images.Remove(image);
            images.Insert(0, image);
        }

        return images.Select(x => new Uri(x.FullName)).ToArray();
    }
}