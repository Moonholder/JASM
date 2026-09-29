using CommunityToolkit.WinUI;
using GIMI_ModManager.WinUI.Helpers.Xaml;
using GIMI_ModManager.WinUI.Models;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.ViewModels;
using GIMI_ModManager.WinUI.ViewModels.SubVms;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using Windows.ApplicationModel.DataTransfer;

namespace GIMI_ModManager.WinUI.Views;

public sealed partial class CharactersPage : Page
{
    private readonly NotificationManager _notificationService = App.GetService<NotificationManager>();
    public CharactersViewModel ViewModel { get; set; } = null!;

    public CharactersPage()
    {
        this.NavigationCacheMode = NavigationCacheMode.Disabled;

        ViewModel = App.GetService<CharactersViewModel>();
        InitializeComponent();

        Loaded += CharactersPage_Loaded;
        Unloaded += CharactersPage_Unloaded;

        ViewModel.OnScrollToCharacter += ViewModel_OnScrollToCharacter;
    }

    private void CharactersPage_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus(FocusState.Keyboard);
        if (ViewModel != null)
        {
            ViewModel.SimpleSelectProcessDialogVM.Dialog = SelectProcessDialog;
        }
    }

    private void CharactersPage_Unloaded(object sender, RoutedEventArgs e)
    {
        Bindings.StopTracking();
        ViewModel = null!;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        if (ViewModel != null)
        {
            ViewModel.OnScrollToCharacter -= ViewModel_OnScrollToCharacter;

            if (ViewModel.SimpleSelectProcessDialogVM != null)
            {
                ViewModel.SimpleSelectProcessDialogVM.Dialog = null!;
            }
        }
    }

    private async void ViewModel_OnScrollToCharacter(object? sender, ScrollToCharacterArgs e)
    {
        var character = e?.Character?.Character;
        if (character == null!) return;

        if (ViewModel.Characters == null || ViewModel.Characters.Count == 0) return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        try
        {
            await CharactersGridView.AwaitUiElementLoaded(cts.Token);

            while (CharactersGridView.Items.Count == 0)
            {
                if (cts.Token.IsCancellationRequested) return;

                await Task.Delay(50, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error waiting for CharactersGridView to load or populate");
            return;
        }

        var item = CharactersGridView.Items.FirstOrDefault(x =>
            ((CharacterGridItemModel)x).Character.InternalNameEquals(character));

        if (item is null)
        {
            return;
        }

        try
        {
            await CharactersGridView.SmoothScrollIntoViewWithItemAsync(item, ScrollItemPlacement.Center, disableAnimation: true,
                scrollIfVisible: false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to scroll to character");
        }
    }

    private void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            ViewModel.AutoSuggestBox_TextChanged(sender.Text);
    }

    private void CharacterSearchKeyShortcut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
    }

    private async void SearchBox_OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await ViewModel.SuggestionBox_Chosen((CharacterGridItemModel?)args.ChosenSuggestion);
    }

    private void ImageCommandsFlyout_OnOpening(object? sender, object e)
    {
        if (sender is not MenuFlyout menuFlyout)
            return;

        if (menuFlyout.Target?.DataContext is not CharacterGridItemModel character)
            return;

        ViewModel.OnRightClickContext(character);
    }

    private void SetGridDropHereVisibility(Grid characterThumbnail, Visibility visibility)
    {
        if (characterThumbnail.FindName("DropHereIcon") is FontIcon dropHereIcon)
        {
            dropHereIcon.Visibility = visibility;
        }

        if (characterThumbnail.FindName("DropHereBorder") is Border dropHereBorder)
        {
            dropHereBorder.Visibility = visibility;
        }
    }

    private void CharacterThumbnail_OnDragEnter(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        if (sender is Grid gridItem)
        {
            SetGridDropHereVisibility(gridItem, Visibility.Visible);
        }
    }

    private void CharacterThumbnail_OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is Grid gridItem)
        {
            SetGridDropHereVisibility(gridItem, Visibility.Collapsed);
        }
    }

    private async void CharacterThumbnail_OnDrop(object sender, DragEventArgs e)
    {
        if (sender is Grid gridItem && gridItem.DataContext is CharacterGridItemModel characterGridItem)
        {
            try
            {
                var view = e.DataView;
                if (view.Contains("application/x-td-forward"))
                {
                    if (view.Contains(StandardDataFormats.StorageItems))
                    {
                        var items = await view.GetStorageItemsAsync();
                        if (items.Count > 0)
                        {
                            await ViewModel.ModDroppedOnCharacterAsync(characterGridItem, items);
                        }
                        return;
                    }

                    _notificationService?.ShowNotification(
                        "无法读取文件",
                        "检测到来自 Telegram 的未下载文件。\n请先将文件下载（或另存为）到本地磁盘，然后再拖入。",
                        TimeSpan.FromSeconds(5));
                    return;
                }

                if (view.Contains(StandardDataFormats.WebLink))
                {
                    try
                    {
                        var uri = await view.GetWebLinkAsync();
                        await ViewModel.ModUrlDroppedOnCharacterAsync(characterGridItem, uri);
                        return;
                    }
                    catch
                    {
                    }
                }

                if (view.Contains(StandardDataFormats.StorageItems))
                {
                    var items = await view.GetStorageItemsAsync();
                    if (items.Count > 0)
                    {
                        await ViewModel.ModDroppedOnCharacterAsync(characterGridItem, items);
                    }
                }
                else
                {
                    var formats = string.Join(", ", e.DataView.AvailableFormats);
                    _notificationService?.ShowNotification(
                        "不支持的拖放格式",
                        $"无法识别的拖放格式: {formats}",
                        TimeSpan.FromSeconds(3));
                }
            }
            catch (Exception ex)
            {
                _notificationService?.ShowNotification("拖放发生未知错误", ex.Message, TimeSpan.FromSeconds(3));
            }
            finally
            {
                SetGridDropHereVisibility(gridItem, Visibility.Collapsed);
            }
        }
    }

    private void DragAndDropArea_OnDragEnter(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        Log.Information("DragEnter_DragAndDropArea_OnDragEnter");
    }

    private void DragAndDropArea_OnDrop(object sender, DragEventArgs e)
    {
        Log.Information("Drop_DragAndDropArea_OnDrop");
    }

    private void BitmapImage_OnImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        Log.Error($"Failed to load dock panel element icon. Reason: {e.ErrorMessage}");
    }

    private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.DockPanelVM.ElementSelectionChanged(
            e.AddedItems.OfType<ElementIcon>(),
            e.RemovedItems.OfType<ElementIcon>());
    }

    private void SortingComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SortByCommand.Execute(e.AddedItems.OfType<CharactersViewModel.GridItemSortingMethod>());
    }
}