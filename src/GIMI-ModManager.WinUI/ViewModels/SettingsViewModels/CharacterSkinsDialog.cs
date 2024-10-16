using GIMI_ModManager.WinUI.Services.AppManagement;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GIMI_ModManager.WinUI.ViewModels.SettingsViewModels;

internal class CharacterSkinsDialog
{
    private readonly IWindowManagerService _windowManagerService = App.GetService<IWindowManagerService>();

    public async Task<ContentDialogResult> ShowDialogAsync(bool isEnabled)
    {
        var dialog = new ContentDialog()
        {
            Title = isEnabled ? DisableTitle : EnableTitle,
            Content = new TextBlock()
            {
                Text = isEnabled ? DisableContent : EnableContent,
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true
            },
            DefaultButton = ContentDialogButton.Primary,
            PrimaryButtonText = isEnabled ? DisablePrimaryButtonText : EnablePrimaryButtonText,
            CloseButtonText = "取消"
        };


        return await _windowManagerService.ShowDialogAsync(dialog).ConfigureAwait(false);
    }

    private const string EnableTitle = "启用角色皮肤作为角色?";

    private const string EnableContent =
        "启用这一选项将使 JASM 将游戏中的皮肤视为角色概览中的独立角色.\n" +
        "这可能会成为未来 JASM 的默认设置.\n" +
        "JASM 不会移动你的任何模组，也不会删除任何模组.\n\n" +
        "你确定要启用角色皮肤作为角色吗？JASM 将在之后重新启动...";

    private const string EnablePrimaryButtonText = "启用";

    private const string DisableTitle = "禁用角色皮肤作为角色?";

    private const string DisableContent =
        "如果禁用角色皮肤，JASM 将把游戏中的皮肤视为角色概览中的基础角色的皮肤.\n" +
        "这目前是 JASM 的默认设置\n" +
        "JASM 不会移动任何模组，也不会删除任何模组.\n\n" +
        "你确定要禁用角色皮肤作为角色吗? JASM 将在之后重新启动...";

    private const string DisablePrimaryButtonText = "禁用";
}