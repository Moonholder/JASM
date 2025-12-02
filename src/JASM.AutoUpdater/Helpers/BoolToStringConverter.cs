using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace JASM.AutoUpdater.Helpers;

internal class BoolToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (parameter is string formatString && value is bool boolValue)
        {
            string statusString = boolValue ? "可用" : "不可用";
            return string.Format(formatString, statusString);
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}