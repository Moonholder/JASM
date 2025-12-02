using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace JASM.AutoUpdater.Helpers;

internal class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (parameter is string colors && value is bool boolValue)
        {
            var colorParts = colors.Split('|');
            if (colorParts.Length == 2)
            {
                var trueColor = colorParts[0];
                var falseColor = colorParts[1];

                var colorString = boolValue ? trueColor : falseColor;
                return colorString switch
                {
                    "Red" => new SolidColorBrush(Colors.Red),
                    "Green" => new SolidColorBrush(Colors.Green),
                    "Black" => new SolidColorBrush(Colors.Black),
                    "White" => new SolidColorBrush(Colors.White),
                    _ => new SolidColorBrush(Colors.Black),
                };
            }
        }

        return new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}