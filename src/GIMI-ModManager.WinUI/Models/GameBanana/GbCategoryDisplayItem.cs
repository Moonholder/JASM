using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GIMI_ModManager.WinUI.Models.GameBanana;

public partial class GbCategoryDisplayItem : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string? CategoryId { get; init; }
    public string? IconUrl { get; init; }
    public int ItemCount { get; init; }
    public bool IsSection { get; init; }
    public string ItemCountText => ItemCount > 0 ? ItemCount.ToString() : string.Empty;
    public Visibility HasItemCountText => ItemCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public string IconGlyph => CategoryId == null ? "\uE8FD" : IsSection ? "\uE8B7" : "\uE71D";
    public Windows.UI.Text.FontWeight SectionFontWeight => IsSection
        ? Microsoft.UI.Text.FontWeights.SemiBold
        : Microsoft.UI.Text.FontWeights.Normal;

    [ObservableProperty] private BitmapImage? _iconImage;

    public void LoadIcon()
    {
        Helpers.RemoteImageLoader.LoadInto(IconUrl, 24, img => IconImage = img);
    }
}