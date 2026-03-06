using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace GIMI_ModManager.WinUI.Helpers.Xaml;

public partial class IntToVisibilityInverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int intValue)
        {
            return intValue == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}