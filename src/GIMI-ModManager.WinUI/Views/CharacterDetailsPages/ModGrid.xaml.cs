using CommunityToolkit.WinUI.UI.Controls;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace GIMI_ModManager.WinUI.Views.CharacterDetailsPages;

public sealed partial class ModGrid : UserControl
{
    public DataGrid DataGrid => ModListGrid;

    public ModGrid()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ModGridVM), typeof(ModGrid), new PropertyMetadata(default(ModGridVM), OnViewModelPropertyChanged));

    public ModGridVM ViewModel
    {
        get => (ModGridVM)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ModGrid grid)
        {
            if (e.OldValue is ModGridVM oldVm)
            {
                grid.UnsubscribeEvents(oldVm);
            }
            if (e.NewValue is ModGridVM newVm)
            {
                grid.SubscribeEvents(newVm);
            }
        }
    }

    private void SubscribeEvents(ModGridVM vm)
    {
        vm.SelectModEvent += ViewModelOnSelectModEvent;
        vm.SortEvent += SetSortUiEventHandler;
        vm.OnInitialized += ViewModel_OnInitialized;
        if (vm.IsInitialized)
        {
            ViewModel_OnInitialized(vm, EventArgs.Empty);
        }
    }

    private void UnsubscribeEvents(ModGridVM vm)
    {
        vm.SelectModEvent -= ViewModelOnSelectModEvent;
        vm.SortEvent -= SetSortUiEventHandler;
        vm.OnInitialized -= ViewModel_OnInitialized;
    }

    private void ViewModel_OnInitialized(object? sender, EventArgs e)
    {
        if (ViewModel == null) return;
        var mods = new List<ModRowVM>(ViewModel.GridMods);
        if (mods.All(m => m.Description.IsNullOrEmpty()))
            NotesColumn.Width = DataGridLength.SizeToHeader;
    }

    private void ViewModelOnSelectModEvent(object? sender, ModGridVM.SelectModRowEventArgs e)
    {
        var count = ViewModel?.GridMods.Count ?? 0;
        if (e.Index < 0 || e.Index >= count)
        {
            ModListGrid.SelectedIndex = -1;
            return;
        }

        if (ModListGrid.SelectedIndex == e.Index)
        {
            var primary = ModListGrid.SelectedItem as ModRowVM;
            var allSelected = ModListGrid.SelectedItems.OfType<ModRowVM>().ToList();
            if (primary == null && allSelected.Count > 0)
            {
                primary = allSelected[0];
            }
            ViewModel?.SetSelection(allSelected, primary);
        }
        else
        {
            ModListGrid.SelectedIndex = e.Index;
        }
    }

    private void ModNameCell_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModRowVM row })
        {
            if (ModListGrid.SelectionMode == DataGridSelectionMode.Single)
            {
                ModListGrid.SelectedItem = row;
            }
            else
            {
                var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
                var isCtrl = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
                var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
                var isShift = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

                if (!isCtrl && !isShift)
                {
                    if (ModListGrid.SelectedItem != row || ModListGrid.SelectedItems.Count != 1)
                    {
                        ModListGrid.SelectedItem = row;
                    }
                    else
                    {
                        ViewModel?.SetSelection(ModListGrid.SelectedItems.OfType<ModRowVM>().ToList(), row);
                    }
                }
                else if (isCtrl)
                {
                    if (ModListGrid.SelectedItems.Contains(row))
                        ModListGrid.SelectedItems.Remove(row);
                    else
                        ModListGrid.SelectedItems.Add(row);
                }
            }
        }
    }

    private void ModListGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null) return;

        try
        {
            var primary = ModListGrid.SelectedItem as ModRowVM;
            var allSelected = ModListGrid.SelectedItems.OfType<ModRowVM>().ToList();
            if (primary == null && allSelected.Count > 0)
            {
                primary = allSelected[0];
            }

            vm.SetSelection(allSelected, primary);
        }
        catch (Exception ex)
        {
            Serilog.Log.ForContext<ModGrid>().Error(ex, "Error in ModListGrid_OnSelectionChanged");
        }
    }

    private void SetSortUiEventHandler(object? sender, SortEvent sortEvent)
    {
        var column = DataGrid.Columns.FirstOrDefault(c => c.Tag?.ToString() == sortEvent.SortColumn);
        if (column is null) return;

        column.SortDirection = sortEvent.IsDescending ? DataGridSortDirection.Descending : DataGridSortDirection.Ascending;

        foreach (var dgColumn in ModListGrid.Columns)
        {
            if (dgColumn.Tag?.ToString() != sortEvent.SortColumn)
                dgColumn.SortDirection = null;
        }
    }

    private void OnColumnSort(object? sender, DataGridColumnEventArgs e)
    {
        var sortColumn = e.Column.Tag?.ToString() ?? "";
        var isDescending = e.Column.SortDirection == null || e.Column.SortDirection == DataGridSortDirection.Ascending;
        e.Column.SortDirection = isDescending ? DataGridSortDirection.Descending : DataGridSortDirection.Ascending;

        ViewModel?.SetModSorting(sortColumn, isDescending, true);

        foreach (var dgColumn in ModListGrid.Columns)
        {
            if (dgColumn.Tag?.ToString() != sortColumn)
                dgColumn.SortDirection = null;
        }
    }

    private async void ModListGrid_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel != null)
            await ViewModel.OnKeyDown_EventHandlerAsync(e.Key).ConfigureAwait(false);
    }

    private void NotificationButton_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.DataContext is not ModRowVM_ModNotificationVM modNotification) return;
        if (modNotification.AttentionType != AttentionType.UpdateAvailable) return;

        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }

    private void NotificationButton_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    }

    private void Notification_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.DataContext is not ModRowVM_ModNotificationVM modNotification) return;
        if (modNotification.AttentionType != AttentionType.UpdateAvailable) return;

        if (ViewModel != null && ViewModel.OpenNewModsWindowCommand.CanExecute(modNotification))
            ViewModel.OpenNewModsWindowCommand.ExecuteAsync(modNotification);
    }

    private async void ModRow_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: ModRowVM row })
        {
            args.Cancel = true;
            return;
        }

        var selectedRows = ModListGrid.SelectedItems.OfType<ModRowVM>().ToList();
        if (!selectedRows.Contains(row))
        {
            selectedRows = [row];
        }

        var deferral = args.GetDeferral();
        try
        {
            var storageItems = new List<IStorageItem>();
            foreach (var r in selectedRows)
            {
                if (!string.IsNullOrEmpty(r.AbsFolderPath))
                {
                    try
                    {
                        var folder = await StorageFolder.GetFolderFromPathAsync(r.AbsFolderPath);
                        storageItems.Add(folder);
                    }
                    catch
                    {
                    }
                }
            }

            if (storageItems.Count > 0)
            {
                args.Data.SetStorageItems(storageItems);
                args.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Link;
                args.Data.Properties.Add("IsModManagerDragOut", true);
            }
            else
            {
                args.Cancel = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ModListGrid_OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel)
            return;

        var mod = (ModRowVM)e.Row.DataContext;

        if (e.Column.Tag?.ToString() == nameof(ModRowVM.Author))
        {
            if (IsValueUpdated(mod.Author, out var newValue))
                _ = UpdateSettingsAsync(new UpdateSettingsRequest { SetAuthor = newValue });
        }
        else if (e.Column.Tag?.ToString() == nameof(ModRowVM.DisplayName))
        {
            if (IsValueUpdated(mod.DisplayName, out var newValue))
                _ = UpdateSettingsAsync(new UpdateSettingsRequest { SetCustomName = newValue });
        }
        else if (e.Column.Tag?.ToString() == nameof(ModRowVM.Description))
        {
            if (IsValueUpdated(mod.Description, out var newValue))
                _ = UpdateSettingsAsync(new UpdateSettingsRequest { SetDescription = newValue });
        }

        bool IsValueUpdated(string oldValue, out string newValue)
        {
            var textBox = (TextBox)e.EditingElement;
            newValue = textBox.Text.Trim();
            return newValue != oldValue;
        }

        async Task UpdateSettingsAsync(UpdateSettingsRequest updateRequest)
        {
            if (ViewModel != null)
            {
                var arg = new ModGridVM.UpdateModSettingsArgument(mod, updateRequest);
                await ViewModel.UpdateModSettingsAsync(arg).ConfigureAwait(false);
            }
        }
    }

    private void ModListGrid_OnCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        if (e.Row.DataContext is ModRowVM mod) mod.TriggerPropertyChanged(string.Empty);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel != null)
            {
                UnsubscribeEvents(ViewModel);
            }

            ModListGrid.ItemsSource = null;
            Bindings.StopTracking();
        }
        catch (Exception)
        {
        }
    }
}