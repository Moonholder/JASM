using CommunityToolkit.WinUI.UI.Controls;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ModRowVM = GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels.ModRowVM;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GIMI_ModManager.WinUI.Views.CharacterDetailsPages;

public sealed partial class ModGrid : UserControl
{
    public DataGrid DataGrid { get; }

    public ModGrid()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataGrid = ModListGrid;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            UnsubscribeEvents(ViewModel);
            SubscribeEvents(ViewModel);
        }
    }


    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ModGridVM), typeof(ModGrid), new PropertyMetadata(null, OnViewModelChanged));

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ModGrid)d;
        var oldVm = e.OldValue as ModGridVM;
        var newVm = e.NewValue as ModGridVM;

        if (oldVm != null)
        {
            control.UnsubscribeEvents(oldVm);
        }

        if (newVm != null)
        {
            control.SubscribeEvents(newVm);
        }

        control.OnViewModelSetHandler(newVm);
    }

    public ModGridVM ViewModel
    {
        get => (ModGridVM)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private void SubscribeEvents(ModGridVM vm)
    {
        if (vm == null) return;
        vm.SelectModEvent += ViewModelOnSelectModEvent;
        vm.SortEvent += SetSortUiEventHandler;
        vm.OnInitialized += ViewModel_OnInitialized;
    }

    private void UnsubscribeEvents(ModGridVM vm)
    {
        if (vm == null) return;
        vm.SelectModEvent -= ViewModelOnSelectModEvent;
        vm.SortEvent -= SetSortUiEventHandler;
        vm.OnInitialized -= ViewModel_OnInitialized;
    }

    private async void ModListGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null) return;

        try
        {
            await vm.ToggleLock.LockAsync();
            vm.ToggleLock.Release();

            if (ViewModel == null) return;

            ViewModel.SelectionChanged_EventHandler(
                e.AddedItems.OfType<ModRowVM>().ToArray(),
                e.RemovedItems.OfType<ModRowVM>().ToArray());
        }
        catch (Exception)
        {
        }
    }

    private void OnViewModelSetHandler(ModGridVM? viewModel)
    {
        if (viewModel is null) return;

        if (viewModel.IsInitialized)
        {
            ViewModel_OnInitialized(viewModel, EventArgs.Empty);
        }
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
        if (ModListGrid is null) return;
        if (e.Index >= 0 && e.Index < ModListGrid.ItemsSource?.OfType<object>().Count())
        {
            DataGrid.SelectedIndex = e.Index;
        }
        else
        {
            DataGrid.SelectedIndex = -1;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ViewModel is not null)
            {
                UnsubscribeEvents(ViewModel);
            }

            DataGrid.ItemsSource = null;
            DataGrid.DataContext = null;

            Bindings.StopTracking();
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private void SetSortUiEventHandler(object? sender, SortEvent sortEvent)
    {
        var column = DataGrid.Columns.FirstOrDefault(c => c.Tag?.ToString() == sortEvent.SortColumn);
        if (column is null) return;

        column.SortDirection = sortEvent.IsDescending ? DataGridSortDirection.Descending : DataGridSortDirection.Ascending;

        foreach (var dgColumn in ModListGrid.Columns)
            if (dgColumn.Tag?.ToString() != sortEvent.SortColumn)
                dgColumn.SortDirection = null;
    }

    private void OnColumnSort(object? sender, DataGridColumnEventArgs e)
    {
        var sortColumn = e.Column.Tag?.ToString() ?? "";
        var isDescending = e.Column.SortDirection == null || e.Column.SortDirection == DataGridSortDirection.Ascending;
        e.Column.SortDirection = isDescending ? DataGridSortDirection.Descending : DataGridSortDirection.Ascending;

        ViewModel?.SetModSorting(sortColumn, isDescending, true);

        foreach (var dgColumn in ModListGrid.Columns)
            if (dgColumn.Tag?.ToString() != sortColumn)
                dgColumn.SortDirection = null;
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

    private void ModListGrid_OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel)
            return;

        var mod = (ModRowVM)e.Row.DataContext;


        if (e.Column.Tag.ToString() == nameof(ModRowVM.Author))
        {
            if (IsValueUpdated(mod.Author, out var newValue))
                _ = UpdateModSettingsAsync(new UpdateSettingsRequest { SetAuthor = newValue });
        }
        else if (e.Column.Tag.ToString() == nameof(ModRowVM.DisplayName))
        {
            if (IsValueUpdated(mod.DisplayName, out var newValue))
                _ = UpdateModSettingsAsync(new UpdateSettingsRequest { SetCustomName = newValue });
        }
        else if (e.Column.Tag.ToString() == nameof(ModRowVM.Description))
        {
            if (IsValueUpdated(mod.Description, out var newValue))
                _ = UpdateModSettingsAsync(new UpdateSettingsRequest { SetDescription = newValue });
        }

        return;

        bool IsValueUpdated(string oldValue, out string newValue)
        {
            var textBox = (TextBox)e.EditingElement;
            newValue = textBox.Text.Trim();
            return newValue != oldValue;
        }

        async Task UpdateModSettingsAsync(UpdateSettingsRequest updateRequest)
        {
            var arg = new ModGridVM.UpdateModSettingsArgument(mod, updateRequest);
            if (mod.UpdateModSettingsCommand.CanExecute(arg))
                await mod.UpdateModSettingsCommand.ExecuteAsync(arg).ConfigureAwait(false);
        }
    }

    private void ModListGrid_OnCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        if (e.Row.DataContext is ModRowVM mod) mod.TriggerPropertyChanged(string.Empty);
    }

    private void Notification_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.DataContext is not ModRowVM_ModNotificationVM modNotification) return;
        if (modNotification.AttentionType != AttentionType.UpdateAvailable) return;

        if (ViewModel != null && ViewModel.OpenNewModsWindowCommand.CanExecute(modNotification))
            ViewModel.OpenNewModsWindowCommand.ExecuteAsync(modNotification);
    }
}