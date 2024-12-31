using Microsoft.UI.Xaml.Data;

namespace GIMI_ModManager.WinUI.Helpers.Xaml
{
    public class StringNullOrEmptyToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value;
        }
    }
}