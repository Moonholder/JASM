using GIMI_ModManager.WinUI.ViewModels.CharacterManagerViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GIMI_ModManager.WinUI.Views.CharacterManager;

public sealed partial class CreateCharacterPage : Page
{
    public CreateCharacterViewModel ViewModel { get; } = App.GetService<CreateCharacterViewModel>();

    public CreateCharacterPage()
    {
        this.InitializeComponent();
    }

    private void RemoveKey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            ViewModel.RemoveKeyCommand.Execute(key);
        }
    }
}