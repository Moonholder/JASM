using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GIMI_ModManager.WinUI.Services.Notifications;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace GIMI_ModManager.WinUI.ViewModels;

public partial class NotificationsViewModel : ObservableRecipient
{
    public readonly NotificationManager NotificationManager;

    [ObservableProperty]
    private string _logFilePath;

    public NotificationsViewModel(NotificationManager notificationManager)
    {
        NotificationManager = notificationManager;

        var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        var today = DateTime.Now.ToString("yyyyMMdd");
        var fileName = $"log{today}.txt";

        LogFilePath = Path.Combine(logDir, fileName);
    }

    [RelayCommand]
    private async Task OpenLogFolderAsync()
    {
        var logDir = Path.GetDirectoryName(LogFilePath);
        if (Directory.Exists(logDir))
        {
            await Launcher.LaunchFolderPathAsync(logDir);
        }
        else
        {
            NotificationManager.ShowNotification("Error", "Log folder does not exist yet.", null);
        }
    }
}