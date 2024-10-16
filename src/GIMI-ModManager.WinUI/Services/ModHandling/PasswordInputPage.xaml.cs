using Microsoft.UI.Xaml.Controls;

namespace GIMI_ModManager.WinUI.Services.ModHandling
{
    public sealed partial class PasswordInputPage : Page
    {
        public PasswordInputPage()
        {
            this.InitializeComponent();
        }

        public string GetPassword()
        {
            // 返回原始文本
            return PasswordBox.Text;
        }
    }
}