using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace JASM.AutoUpdater.Helpers;

internal class BoolToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool boolValue = (bool)(value ?? false);
        bool invert = parameter as string == "Invert";

        if (invert)
        {
            boolValue = !boolValue;
        }
        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var visibility = value as Visibility?;
        return visibility != null && visibility == Visibility.Visible;
    }
}