using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace JASM.AutoUpdater.Helpers;

internal class ToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (parameter is string format && !string.IsNullOrEmpty(format))
        {
            return string.Format(format, value);
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}