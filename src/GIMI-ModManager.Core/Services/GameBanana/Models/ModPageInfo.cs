using GIMI_ModManager.Core.Services.GameBanana.ApiModels;
using static System.Net.Mime.MediaTypeNames;

namespace GIMI_ModManager.Core.Services.GameBanana.Models;

public class ModPreviewImageInfo
{
    public Uri Original { get; init; } = null!;
    public Uri? Thumb100 { get; init; }
    public Uri? Thumb530 { get; init; }
}

public class ModPageInfo
{
    public ModPageInfo(ApiModProfile apiModProfile)
    {
        ModId = apiModProfile.ModId.ToString();
        ModPageUrl = Uri.TryCreate(apiModProfile.ModPageUrl, UriKind.Absolute, out var modPageUrl) ? modPageUrl : null;
        ModName = apiModProfile.ModName;
        AuthorName = apiModProfile.Author?.AuthorName;
        AuthorAvatarUrl = apiModProfile.Author?.AvatarImageUrl;
        CategoryName = apiModProfile.Category?.Name;
        ViewCount = apiModProfile.ViewCount;
        List<ModPreviewImageInfo> previewImageUrls = [];

        // v13 serves previews as "_aPreviewContent" keyed by content type;
        // the profile uses the "screenshots" array.
        var screenshotImages = apiModProfile.PreviewContent?.GetValueOrDefault("screenshots");
        if (screenshotImages != null)
        {
            foreach (var previewImage in screenshotImages)
            {
                if (previewImage == null)
                    continue;

                var originalUrl = TryBuildImageUrl(previewImage, previewImage.File);
                if (originalUrl == null)
                    continue;

                var imgInfo = new ModPreviewImageInfo
                {
                    Original = originalUrl,
                    Thumb100 = TryBuildImageUrl(previewImage, previewImage.File100 ?? previewImage.File220 ?? previewImage.File)
                               ?? originalUrl,
                    Thumb530 = TryBuildImageUrl(previewImage, previewImage.File530 ?? previewImage.File220 ?? previewImage.File)
                               ?? originalUrl
                };

                previewImageUrls.Add(imgInfo);
            }
        }

        PreviewImages = previewImageUrls.AsReadOnly();
        Description = apiModProfile.Description;

        Files = apiModProfile.Files?.Select(apiModFileInfo => new ModFileInfo(apiModFileInfo, ModId)
        {
            // v13 dropped the per-file version; fall back to the mod-level one.
            Version = string.IsNullOrEmpty(apiModFileInfo.Version) ? apiModProfile.Version : apiModFileInfo.Version
        }).ToList() ?? [];
    }

    private static Uri? TryBuildImageUrl(ApiPreviewImage image, string? file)
    {
        if (string.IsNullOrEmpty(file))
            return null;

        if (!Uri.TryCreate(image.BaseUrl + "/" + file, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("images.gamebanana.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri;
    }

    public string ModId { get; init; }
    public Uri? ModPageUrl { get; init; }
    public string? ModName { get; init; }
    public string? AuthorName { get; init; }
    public string? AuthorAvatarUrl { get; init; }
    public string? CategoryName { get; init; }
    public int ViewCount { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<ModPreviewImageInfo> PreviewImages { get; init; }

    public IReadOnlyList<ModFileInfo> Files { get; init; }

    public System.Collections.Generic.IReadOnlyList<ApiModUpdate>? Updates { get; set; }
}