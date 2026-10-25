using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GIMI_ModManager.WinUI.Models.GameBanana;

public partial class GbPreviewImageItem : ObservableObject
{
    public string FullUrl { get; init; } = string.Empty;
    public string Thumbnail100Url { get; init; } = string.Empty;
    public string Thumbnail530Url { get; init; } = string.Empty;

    [ObservableProperty] private BitmapImage? _imageSource;
    [ObservableProperty] private BitmapImage? _fullImageSource;

    [ObservableProperty] private bool _isFullImageLoading;
    private bool _hasStartedLoadingFull;

    public void Load()
    {
        Helpers.RemoteImageLoader.LoadInto(Thumbnail100Url, 100, img => ImageSource = img, "_100");
    }

    public void LoadFullImage()
    {
        if (FullImageSource != null || _hasStartedLoadingFull) return;
        _hasStartedLoadingFull = true;
        IsFullImageLoading = true;
        Helpers.RemoteImageLoader.LoadInto(FullUrl, 1920, img =>
        {
            FullImageSource = img;
            IsFullImageLoading = false;
        }, "_1920");
    }
}