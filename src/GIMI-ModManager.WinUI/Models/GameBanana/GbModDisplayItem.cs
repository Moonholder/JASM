using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GIMI_ModManager.WinUI.Models.GameBanana;

public partial class GbModDisplayItem : ObservableObject
{
    public int ModId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ModelName { get; init; } = "Mod";
    public string? ProfileUrl { get; init; }
    public string ThumbnailUrl { get; init; } = string.Empty;
    public string SfwThumbnailUrl { get; init; } = string.Empty;
    public string AuthorName { get; init; } = "Unknown";
    public string AuthorAvatarUrl { get; init; } = string.Empty;
    public int LikeCount { get; init; }
    public int DownloadCount { get; init; }
    public int CommentCount { get; init; }
    public long DateAdded { get; init; }
    public long DateUpdated { get; init; }
    [ObservableProperty] private bool _isNsfw;
    [ObservableProperty] private GbNsfwDisplayPolicy _nsfwPolicy;

    /// <summary>
    /// Small corner badge shown on blurred NSFW cards (Blur display policy).
    /// </summary>
    public Visibility NsfwBlurBadgeVisible => IsNsfw && NsfwPolicy == GbNsfwDisplayPolicy.Blur
        ? Visibility.Visible
        : Visibility.Collapsed;

    /// <summary>
    /// Corner badge shown when NSFW content is fully revealed (Show policy).
    /// </summary>
    public Visibility IsNsfwBadgeVisible => IsNsfw && NsfwPolicy == GbNsfwDisplayPolicy.Show ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Type label to show on the card (empty for regular Mods).
    /// </summary>
    public string TypeLabel => ModelName switch
    {
        "Mod" => string.Empty,
        "Wip" => "WIP",
        _ => ModelName
    };

    public Visibility TypeLabelVisibility => string.IsNullOrEmpty(TypeLabel)
        ? Visibility.Collapsed
        : Visibility.Visible;

    [ObservableProperty] private BitmapImage? _thumbnailImage;
    [ObservableProperty] private BitmapImage? _authorAvatarImage;

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(GbModDisplayItem))]
    public void LoadThumbnail()
    {
        if (IsNsfw && NsfwPolicy == GbNsfwDisplayPolicy.Blur)
        {
            if (!string.IsNullOrEmpty(SfwThumbnailUrl))
            {
                Helpers.RemoteImageLoader.LoadInto(SfwThumbnailUrl, 380, img => ThumbnailImage = img, "_sfw");
            }
            else
            {
                Helpers.RemoteImageLoader.LoadIntoBlurred(ThumbnailUrl, 120, img => ThumbnailImage = img, "_blur");
            }
        }
        else
        {
            Helpers.RemoteImageLoader.LoadInto(ThumbnailUrl, 380, img => ThumbnailImage = img);
        }

        if (!string.IsNullOrEmpty(AuthorAvatarUrl))
        {
            Helpers.RemoteImageLoader.LoadInto(AuthorAvatarUrl, 24, img => AuthorAvatarImage = img);
        }
    }

    /// <summary>
    /// Drops the loaded bitmaps so a recycled card container does not keep
    /// decoded images alive. Images reload from the memory/disk cache when
    /// the card is realized again.
    /// </summary>
    public void ReleaseImages()
    {
        if (ThumbnailImage != null) ThumbnailImage = null;
        if (AuthorAvatarImage != null) AuthorAvatarImage = null;
    }

    public string FormattedDownloads => DownloadCount >= 1000
        ? $"{DownloadCount / 1000.0:F1}k"
        : DownloadCount.ToString();

    public string FormattedLikes => LikeCount >= 1000
        ? $"{LikeCount / 1000.0:F1}k"
        : LikeCount.ToString();

    public string FormattedComments => CommentCount >= 1000
        ? $"{CommentCount / 1000.0:F1}k"
        : CommentCount.ToString();

    public string FormattedDate
    {
        get
        {
            long ts = DateUpdated > DateAdded ? DateUpdated : DateAdded;
            if (ts == 0) return string.Empty;

            var dt = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
            var diff = DateTime.Now - dt;
            return Helpers.FormaterHelpers.FormatTimeSinceAdded(diff);
        }
    }
}