using System.Text;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.WinUI.Services.AppManagement;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NotificationManager = GIMI_ModManager.WinUI.Services.Notifications.NotificationManager;

namespace GIMI_ModManager.WinUI.ViewModels.SettingsViewModels;

internal class ClearEmptyFoldersDialog
{
    private readonly ISkinManagerService _skinManagerService = App.GetService<ISkinManagerService>();
    private readonly NotificationManager _notificationManager = App.GetService<NotificationManager>();
    private readonly IWindowManagerService _windowManagerService = App.GetService<IWindowManagerService>();


    public async Task ShowDialogAsync()
    {
        var dialog = new ContentDialog()
        {
            Title = "清除空文件夹",
            Content = new TextBlock()
            {
                Text =
                    "如果文件夹为空或只包含.JASM_文件/文件夹，这将删除角色的mod列表中的所有空文件夹\n" +
                    "如果角色文件夹是空的，那么它也会被删除.\n" +
                    "mod文件夹根目录下的空文件夹也会被删除",
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true
            },
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        };


        var result = await _windowManagerService.ShowDialogAsync(dialog);

        if (result == ContentDialogResult.Primary)
        {
            var deletedFolders = await Task.Run(() => _skinManagerService.CleanCharacterFolders());
            var sb = new StringBuilder();
            sb.AppendLine("Deleted folders:");
            foreach (var folder in deletedFolders)
            {
                sb.AppendLine(folder.FullName);
            }

            var message = sb.ToString();

            _notificationManager.ShowNotification("空文件夹清理完成", message, TimeSpan.FromSeconds(5));
        }
    }
}