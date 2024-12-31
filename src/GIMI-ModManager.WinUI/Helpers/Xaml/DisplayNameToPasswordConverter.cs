using GIMI_ModManager.WinUI.Models;
using Microsoft.UI.Xaml.Data;

namespace GIMI_ModManager.WinUI.Helpers.Xaml
{
    public class DisplayNameToPasswordConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is PasswordEntry passwordEntry)
            {
                if (string.IsNullOrEmpty(passwordEntry.DisplayName))
                {
                    return passwordEntry.Password;
                }
                return passwordEntry.DisplayName;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value;
        }
    }
}