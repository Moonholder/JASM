using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace GIMI_ModManager.WinUI.Helpers.Xaml;

public class BoolToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool booleanValue)
        {
            if (parameter is string param && param.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            {
                return booleanValue ? Visibility.Collapsed : Visibility.Visible;
            }

            return booleanValue ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var visibility = value as Visibility?;
        return visibility != null && visibility == Visibility.Visible;
    }
}