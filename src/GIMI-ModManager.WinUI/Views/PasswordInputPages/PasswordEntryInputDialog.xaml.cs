using GIMI_ModManager.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GIMI_ModManager.WinUI.Views.PasswordInputPages
{
    public sealed partial class PasswordEntryInputDialog : ContentDialog
    {
        public PasswordEntry Entry { get; set; }
        public PasswordManagerDialog ParentDialog { get; set; }

        public PasswordEntryInputDialog()
        {
            InitializeComponent();
            PrimaryButtonClick += ContentDialog_PrimaryButtonClick;
            SecondaryButtonClick += ContentDialog_SecondaryButtonClick;
        }

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(PasswordTextBox.Text))
            {
                args.Cancel = true;
                return;
            }
            Entry.Password = PasswordTextBox.Text;
            Entry.DisplayName = DisplayNameTextBox.Text;
            ContentDialog_SecondaryButtonClick(sender, args);
        }

        private async void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            Hide();
            await ParentDialog.ShowAsync();
        }

        public void SetPasswordEntry(PasswordEntry entry)
        {
            Entry = entry;
            PasswordTextBox.Text = entry.Password;
            DisplayNameTextBox.Text = entry.DisplayName;
        }
    }
}