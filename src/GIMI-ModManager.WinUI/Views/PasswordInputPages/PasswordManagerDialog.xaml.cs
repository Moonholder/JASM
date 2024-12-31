using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using GIMI_ModManager.WinUI.Models;
using GIMI_ModManager.WinUI.Models.Settings;
using GIMI_ModManager.WinUI.Contracts.Services;

namespace GIMI_ModManager.WinUI.Views.PasswordInputPages
{
    public sealed partial class PasswordManagerDialog : ContentDialog
    {
        public ObservableCollection<PasswordEntry> PasswordEntries { get; set; }
        public ContentDialog ParentDialog { get; set; }
        public PasswordManagerDialog()
        {
            InitializeComponent();
            Loaded += PasswordManagerDialog_Loaded;
            PrimaryButtonClick += PasswordManagerDialog_PrimaryButtonClick;
            SecondaryButtonClick += PasswordManagerDialog_SecondaryButtonClick;
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            var inputDialog = new PasswordEntryInputDialog()
            {
                XamlRoot = XamlRoot,
                ParentDialog = this,
                RequestedTheme = RequestedTheme
            };
            var addedEntry = new PasswordEntry();
            inputDialog.SetPasswordEntry(addedEntry);
            await inputDialog.ShowAsync();
            if (string.IsNullOrEmpty(addedEntry.Password)) return;
            PasswordEntries.Add(inputDialog.Entry);
            Flush();
        }

        private async void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (PasswordListView.SelectedItem is PasswordEntry selectedEntry)
            {
                Hide();
                var inputDialog = new PasswordEntryInputDialog()
                {
                    XamlRoot = XamlRoot,
                    ParentDialog = this,
                    RequestedTheme = RequestedTheme
                };
                inputDialog.SetPasswordEntry(selectedEntry);
                await inputDialog.ShowAsync();
                Flush();
            }
        }

        private void Flush()
        {
            PasswordListView.ItemsSource = null;
            PasswordListView.ItemsSource = PasswordEntries;
        }

        private void PasswordListView_DoubleTapped(object sender, RoutedEventArgs e)
        {
            EditButton_Click(sender, e);
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (PasswordListView.SelectedItem is PasswordEntry selectedEntry)
            {
                PasswordEntries.Remove(selectedEntry);
            }
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            var index = PasswordListView.SelectedIndex;
            if (index > 0)
            {
                var item = PasswordEntries[index];
                PasswordEntries.RemoveAt(index);
                PasswordEntries.Insert(index - 1, item);
                PasswordListView.SelectedIndex = index - 1;
            }
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            var index = PasswordListView.SelectedIndex;
            if (index >= 0 && index < PasswordEntries.Count - 1)
            {
                var item = PasswordEntries[index];
                PasswordEntries.RemoveAt(index);
                PasswordEntries.Insert(index + 1, item);
                PasswordListView.SelectedIndex = index + 1;
            }
        }

        private void PasswordManagerDialog_Loaded(object sender, RoutedEventArgs e)
        {
            PasswordListView.ItemsSource = PasswordEntries;
        }
        private void PasswordManagerDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {

            PasswordManagerDialog_SecondaryButtonClick(sender, args);
        }

        private async void PasswordManagerDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            Hide();
            await ParentDialog.ShowAsync();
        }
    }
}