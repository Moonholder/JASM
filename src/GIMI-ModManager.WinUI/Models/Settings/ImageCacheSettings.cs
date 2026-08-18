using Newtonsoft.Json;

namespace GIMI_ModManager.WinUI.Models.Settings;

/// <summary>
/// App-scoped limit for the shared thumbnail/preview image cache. Unlike the
/// per-game mod archive cache, images are shared across games (GameBanana image
/// URLs are globally unique), so the budget is a single global value.
/// </summary>
internal class ImageCacheSettings
{
    [JsonIgnore] public const string Key = "ImageCacheSettings";
    public const int DefaultMaxImageCacheMb = 256;

    public int MaxImageCacheMb { get; set; } = DefaultMaxImageCacheMb;
}
