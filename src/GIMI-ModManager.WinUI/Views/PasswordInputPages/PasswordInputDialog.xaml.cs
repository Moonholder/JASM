using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Services;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Models;
using GIMI_ModManager.WinUI.Models.Settings;
using Microsoft.UI.Xaml.Controls;

namespace GIMI_ModManager.WinUI.Views.PasswordInputPages
{
    public sealed partial class PasswordInputDialog : ContentDialog
    {
        public CommonPasswordOptions ViewModel { get; set; }
        private TaskCompletionSource<DragAndDropScanResult> Tcs { get; }
        private DragAndDropScanner Scanner { get; }

        private readonly string FilePath;

        private readonly Services.Notifications.NotificationManager _notificationManager;

        private readonly ILanguageLocalizer _localizer;
        public PasswordInputDialog(TaskCompletionSource<DragAndDropScanResult> tsc, DragAndDropScanner scanner, string filePath,
            Services.Notifications.NotificationManager notificationManager, ILanguageLocalizer localizer)
        {
            Tcs = tsc;
            Scanner = scanner;
            FilePath = filePath;
            _notificationManager = notificationManager;
            _localizer = localizer;
            InitializeComponent();
            XamlRoot = App.MainWindow.Content.XamlRoot;
            PasswordBox.SelectionChanged += PasswordComboBox_SelectionChanged;
            PrimaryButtonClick += PasswordInputDialog_PrimaryButtonClick;
            SecondaryButtonClick += PasswordInputDialog_SecondaryButtonClick;
            CloseButtonClick += PasswordInputDialog_CloseButtonClick;
            Loaded += PasswordInputDialog_Loaded;
        }

        public string GetPassword()
        {
            return PasswordBox.Text;
        }
        private void PasswordComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PasswordBox.SelectedItem is PasswordEntry selectedEntry)
            {
                PasswordBox.Text = selectedEntry.Password;
            }
        }

        private void PasswordInputDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var password = GetPassword();
            if (string.IsNullOrEmpty(password))
            {
                args.Cancel = true;
                return;
            }

            var extractResult = Scanner.ScanAndGetContents(FilePath, password);
            if (extractResult != null && extractResult.exitedCode != 0)
            {
                args.Cancel = true;
                _notificationManager.ShowNotification(
                        _localizer.GetLocalizedStringOrDefault("/CharactersPage/PasswordDialog_ExtractFailedTitle", "Extraction Failed"),
                        _localizer.GetLocalizedStringOrDefault("/CharactersPage/PasswordDialog_ExtractFailedMessage", "Incorrect password, please try another one"),
                        TimeSpan.FromSeconds(5));
                return;
            }
            Tcs.SetResult(extractResult);
            SaveCommonPasswordOptions();
        }

        private void PasswordInputDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            Tcs.SetResult(null);
            SaveCommonPasswordOptions();
        }

        private async void PasswordInputDialog_CloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            Hide();
            var dialog = new PasswordManagerDialog
            {
                XamlRoot = XamlRoot,
                ParentDialog = this,
                PasswordEntries = ViewModel.PasswordEntries,
                RequestedTheme = RequestedTheme,
            };

            await dialog.ShowAsync();
        }

        private async void PasswordInputDialog_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (ViewModel == null)
            {
                ViewModel = await App.GetService<ILocalSettingsService>().ReadOrCreateSettingAsync<CommonPasswordOptions>(CommonPasswordOptions.Key);
                ViewModel.PasswordEntries ??= [];
                ViewModel.LastSelectedPassword ??= "";
            }

            if (string.IsNullOrEmpty(PasswordBox.Text))
            {
                PasswordBox.Text = ViewModel.LastSelectedPassword;
            }
            PasswordBox.ItemsSource = null;
            PasswordBox.ItemsSource = ViewModel.PasswordEntries;
        }

        private void SaveCommonPasswordOptions()
        {
            if (!string.IsNullOrEmpty(GetPassword()))
            {
                ViewModel.LastSelectedPassword = GetPassword();
            }
            App.GetService<ILocalSettingsService>().SaveSettingAsync(CommonPasswordOptions.Key, ViewModel);
        }
    }
}