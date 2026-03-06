using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;

namespace GIMI_ModManager.WinUI.Converters;

/// <summary>
/// Converts a URL string to a BitmapImage for use in Image.Source bindings.
/// WinUI 3 Desktop does not have a built-in string→ImageSource converter,
/// so this is required for any Image bound to a URL string.
/// </summary>
public class StringToImageSourceConverter : IValueConverter
{
    private static readonly ILogger Logger = Log.ForContext<StringToImageSourceConverter>();

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string url && !string.IsNullOrEmpty(url))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.ImageFailed += (s, e) =>
                {
                    Logger.Warning("BitmapImage failed to load: {ErrorMessage} | URL: {Url}",
                        e.ErrorMessage, url);
                };
                if (parameter is string widthStr && int.TryParse(widthStr, out var width))
                {
                    bmp.DecodePixelWidth = width;
                    bmp.DecodePixelType = DecodePixelType.Logical;
                }
                bmp.UriSource = new Uri(url);
                return bmp;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "StringToImageSourceConverter failed for URL: {Url}", url);
                return null;
            }
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}