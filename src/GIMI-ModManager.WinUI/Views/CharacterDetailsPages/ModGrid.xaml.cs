using CommunityToolkit.WinUI.UI.Controls;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.Services.ModHandling;
using GIMI_ModManager.WinUI.Services.Notifications;
using GIMI_ModManager.WinUI.Models;
using GIMI_ModManager.WinUI.ViewModels.CharacterDetailsViewModels.SubViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Numerics;
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
        InitializeHoverPreview();
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

                // When Ctrl or Shift is pressed, delegate selection entirely to DataGrid's native UpdateStateOnTapped
                // to avoid double-toggle cancellation.
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
            }
        }
    }

    private void ModListGrid_OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        HideHoverPreview();

        if (e.OriginalSource is not DependencyObject source)
        {
            e.Handled = true;
            return;
        }

        var clickedRow = FindModRowVM(source);
        if (clickedRow == null)
        {
            e.Handled = true;
            return;
        }

        var selectedRows = ModListGrid.SelectedItems.OfType<ModRowVM>().ToList();
        if (selectedRows.Contains(clickedRow))
        {
            ViewModel?.SetSelection(selectedRows, clickedRow);
        }
        else
        {
            if (ModListGrid.SelectionMode == DataGridSelectionMode.Single)
            {
                ModListGrid.SelectedItem = clickedRow;
            }
            else
            {
                ModListGrid.SelectedItems.Clear();
                ModListGrid.SelectedItem = clickedRow;
            }

            ViewModel?.SetSelection([clickedRow], clickedRow);
        }
    }

    private void ModListGrid_OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        HideHoverPreview();

        if (args.TryGetPosition(ModListGrid, out _))
        {
            if (args.OriginalSource is not DependencyObject source || FindModRowVM(source) == null)
            {
                args.Handled = true;
                return;
            }
        }
    }

    private void ModListGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null) return;

        try
        {
            var allSelected = ModListGrid.SelectedItems.OfType<ModRowVM>().ToList();
            var newlyAdded = e.AddedItems.OfType<ModRowVM>().LastOrDefault();
            var primary = (newlyAdded != null && allSelected.Contains(newlyAdded))
                ? newlyAdded
                : (ModListGrid.SelectedItem as ModRowVM ?? allSelected.FirstOrDefault());

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
        HideHoverPreview();
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
            TeardownHoverPreview();
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

    #region Mod Row Hover Preview

    private static readonly TimeSpan ColdHoverDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan LeaveGraceDelay = TimeSpan.FromMilliseconds(60);

    private readonly DispatcherTimer _hoverTimer = new() { Interval = ColdHoverDelay };
    private readonly DispatcherTimer _leaveGraceTimer = new() { Interval = LeaveGraceDelay };

    private ModRowVM? _pendingHoverRow;
    private ModRowVM? _currentActiveHoverRow;
    private Windows.Foundation.Point _lastPointerPosition;
    private bool _isHoverVisible;

    private SpriteVisual? _shadowVisual;
    private DropShadow? _dropShadow;
    private CompositionRoundedRectangleGeometry? _roundedClipGeometry;

    private DependencyObject? _lastHitSource;
    private bool _lastHitIsInteractive;
    private ModRowVM? _lastHitRowVm;

    private static readonly Dictionary<Uri, (BitmapImage Bitmap, double CardWidth, double CardHeight)> _imageCache = new();
    private static readonly LinkedList<Uri> _imageCacheLru = new();
    private const int MaxCachedImages = 50;

    private const int MaxDecodePixelWidth = 800;

    private static (BitmapImage Bitmap, double CardWidth, double CardHeight) GetOrCreateBitmapImage(Uri uri)
    {
        if (_imageCache.TryGetValue(uri, out var cached))
        {
            _imageCacheLru.Remove(uri);
            _imageCacheLru.AddLast(uri);
            return cached;
        }

        var bitmap = new BitmapImage
        {
            DecodePixelType = DecodePixelType.Physical,
            DecodePixelWidth = MaxDecodePixelWidth,
            UriSource = uri
        };

        var entry = (bitmap, 0.0, 0.0);

        if (_imageCache.Count >= MaxCachedImages)
        {
            var oldest = _imageCacheLru.First?.Value;
            if (oldest != null)
            {
                _imageCacheLru.RemoveFirst();
                _imageCache.Remove(oldest);
            }
        }

        _imageCache[uri] = entry;
        _imageCacheLru.AddLast(uri);
        return entry;
    }

    private static (double cardWidth, double cardHeight) CalculateAdaptiveCardSize(double originalWidth, double originalHeight)
    {
        if (originalWidth <= 0 || originalHeight <= 0)
            return (360, 240);

        double ratio = originalWidth / originalHeight;
        double maxInnerW;
        double maxInnerH;

        if (ratio < 0.85)
        {
            maxInnerW = 280;
            maxInnerH = 400;
        }
        else if (ratio > 1.8)
        {
            maxInnerW = 420;
            maxInnerH = 240;
        }
        else
        {
            maxInnerW = 380;
            maxInnerH = 260;
        }

        double scale = Math.Min(maxInnerW / originalWidth, maxInnerH / originalHeight);
        double imgW = Math.Round(originalWidth * scale);
        double imgH = Math.Round(originalHeight * scale);

        imgW = Math.Max(200, imgW);
        imgH = Math.Max(150, imgH);

        // 7px isolating padding on all 4 sides => +14px for total card frame
        return (imgW + 14, imgH + 14);
    }

    private void InitializeHoverPreview()
    {
        InitializeShadow();
        _hoverTimer.Tick += OnHoverTimerTick;
        _leaveGraceTimer.Tick += OnLeaveGraceTimerTick;
        ModListGrid.PointerEntered += ModListGrid_OnPointerEntered;
        ModListGrid.PointerMoved += ModListGrid_OnPointerMoved;
        ModListGrid.PointerExited += ModListGrid_OnPointerExited;
        ModListGrid.PointerWheelChanged += ModListGrid_OnPointerWheelChanged;
        ModListGrid.PointerPressed += ModListGrid_OnPointerPressed;
    }

    private void InitializeShadow()
    {
        try
        {
            var hostVisual = ElementCompositionPreview.GetElementVisual(HoverPreviewShadowHost);
            var compositor = hostVisual.Compositor;

            _dropShadow = compositor.CreateDropShadow();
            _dropShadow.BlurRadius = 36f;
            _dropShadow.Color = Windows.UI.Color.FromArgb(170, 0, 0, 0);
            _dropShadow.Offset = new Vector3(0, 10, 0);
            _dropShadow.Opacity = 0.85f;

            _shadowVisual = compositor.CreateSpriteVisual();
            _shadowVisual.Shadow = _dropShadow;
            _shadowVisual.Size = new Vector2(360f, 240f);

            ElementCompositionPreview.SetElementChildVisual(HoverPreviewShadowHost, _shadowVisual);

            var borderVisual = ElementCompositionPreview.GetElementVisual(HoverPreviewMainBorder);
            _roundedClipGeometry = compositor.CreateRoundedRectangleGeometry();
            _roundedClipGeometry.CornerRadius = new Vector2(12f, 12f);
            _roundedClipGeometry.Size = new Vector2(360f, 240f);
            borderVisual.Clip = compositor.CreateGeometricClip(_roundedClipGeometry);
        }
        catch (Exception ex)
        {
            Serilog.Log.ForContext<ModGrid>().Warning(ex, "Failed to initialize composition drop shadow for hover preview");
        }
    }

    private void TeardownHoverPreview()
    {
        _hoverTimer.Stop();
        _hoverTimer.Tick -= OnHoverTimerTick;
        _leaveGraceTimer.Stop();
        _leaveGraceTimer.Tick -= OnLeaveGraceTimerTick;
        ModListGrid.PointerEntered -= ModListGrid_OnPointerEntered;
        ModListGrid.PointerMoved -= ModListGrid_OnPointerMoved;
        ModListGrid.PointerExited -= ModListGrid_OnPointerExited;
        ModListGrid.PointerWheelChanged -= ModListGrid_OnPointerWheelChanged;
        ModListGrid.PointerPressed -= ModListGrid_OnPointerPressed;
        HideHoverPreview();

        ElementCompositionPreview.SetElementChildVisual(HoverPreviewShadowHost, null);
        _shadowVisual?.Dispose();
        _shadowVisual = null;
        _dropShadow?.Dispose();
        _dropShadow = null;
        _roundedClipGeometry?.Dispose();
        _roundedClipGeometry = null;
    }

    private void ModListGrid_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _lastPointerPosition = e.GetCurrentPoint(ModListGrid).Position;
        if (ViewModel == null || !ViewModel.IsShowHoverPreviewEnabled || ModListGrid.ContextFlyout?.IsOpen == true)
        {
            HideHoverPreview();
        }
    }

    private void ModListGrid_OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel == null || !ViewModel.IsShowHoverPreviewEnabled || ModListGrid.ContextFlyout?.IsOpen == true)
        {
            HideHoverPreview();
            return;
        }

        var point = e.GetCurrentPoint(ModListGrid);
        _lastPointerPosition = point.Position;

        if (e.OriginalSource is not DependencyObject source)
            return;

        var (isInteractive, rowVm) = ResolveHitTest(source);
        if (isInteractive || rowVm == null)
        {
            if (_isHoverVisible)
            {
                if (!_leaveGraceTimer.IsEnabled)
                {
                    _leaveGraceTimer.Start();
                }
            }
            else
            {
                _hoverTimer.Stop();
                _pendingHoverRow = null;
            }
            return;
        }

        _leaveGraceTimer.Stop();

        if (_isHoverVisible)
        {
            if (_currentActiveHoverRow != rowVm)
            {
                _currentActiveHoverRow = rowVm;
                _pendingHoverRow = rowVm;
                RenderPreviewContent(rowVm);
            }

            UpdateCardPosition(_lastPointerPosition);
        }
        else
        {
            if (_pendingHoverRow != rowVm)
            {
                _pendingHoverRow = rowVm;
                _hoverTimer.Stop();
                _hoverTimer.Start();
            }
        }
    }

    private void OnLeaveGraceTimerTick(object? sender, object e)
    {
        _leaveGraceTimer.Stop();
        HideHoverPreview();
    }

    private void ModListGrid_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        HideHoverPreview();
    }

    private void ModListGrid_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        HideHoverPreview();
    }

    private void ModListGrid_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        HideHoverPreview();
    }

    private void OnHoverTimerTick(object? sender, object e)
    {
        _hoverTimer.Stop();

        if (_pendingHoverRow == null || ViewModel == null || !ViewModel.IsShowHoverPreviewEnabled || ModListGrid.ContextFlyout?.IsOpen == true)
        {
            HideHoverPreview();
            return;
        }

        _currentActiveHoverRow = _pendingHoverRow;
        ShowHoverPreview(_currentActiveHoverRow, _lastPointerPosition);
    }

    private void ShowHoverPreview(ModRowVM row, Windows.Foundation.Point position)
    {
        try
        {
            if (HoverPreviewPopup.XamlRoot == null && XamlRoot != null)
            {
                HoverPreviewPopup.XamlRoot = XamlRoot;
            }

            RenderPreviewContent(row);
            HoverPreviewPopup.IsOpen = true;
            UpdateCardPosition(position);
            _isHoverVisible = true;
        }
        catch
        {
            HideHoverPreview();
        }
    }

    private void RenderPreviewContent(ModRowVM row)
    {
        HoverPreviewTitle.Text = row.DisplayName;
        HoverPreviewAuthor.Text = string.IsNullOrWhiteSpace(row.Author) ? string.Empty : $"By: {row.Author}";
        HoverPreviewAuthor.Visibility = string.IsNullOrWhiteSpace(row.Author) ? Visibility.Collapsed : Visibility.Visible;

        if (row.PreviewImageUri == null)
        {
            HoverPreviewPlaceholder.Visibility = Visibility.Visible;
            HoverPreviewImage.Visibility = Visibility.Collapsed;
            HoverPreviewImage.Source = null;
            ApplyCardSize(360, 240);
            return;
        }

        HoverPreviewPlaceholder.Visibility = Visibility.Collapsed;
        HoverPreviewImage.Visibility = Visibility.Visible;

        var (bitmap, cachedW, cachedH) = GetOrCreateBitmapImage(row.PreviewImageUri);
        HoverPreviewImage.Source = bitmap;

        if (cachedW > 0 && cachedH > 0)
        {
            ApplyCardSize(cachedW, cachedH);
        }
        else if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
        {
            var (w, h) = CalculateAdaptiveCardSize(bitmap.PixelWidth, bitmap.PixelHeight);
            _imageCache[row.PreviewImageUri] = (bitmap, w, h);
            ApplyCardSize(w, h);
        }
        else
        {
            ApplyCardSize(360, 240);
        }
    }

    private void ApplyCardSize(double cardWidth, double cardHeight)
    {
        HoverPreviewCard.Width = cardWidth;
        HoverPreviewCard.Height = cardHeight;

        if (_shadowVisual != null)
        {
            _shadowVisual.Size = new Vector2((float)cardWidth, (float)cardHeight);
        }
        if (_roundedClipGeometry != null)
        {
            _roundedClipGeometry.Size = new Vector2((float)cardWidth, (float)cardHeight);
        }
    }

    private void HoverPreviewImage_OnImageOpened(object sender, RoutedEventArgs e)
    {
        if (HoverPreviewImage.Source is BitmapImage bitmap && _currentActiveHoverRow?.PreviewImageUri != null)
        {
            if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
            {
                var (w, h) = CalculateAdaptiveCardSize(bitmap.PixelWidth, bitmap.PixelHeight);
                _imageCache[_currentActiveHoverRow.PreviewImageUri] = (bitmap, w, h);
                ApplyCardSize(w, h);
            }
        }

        if (_isHoverVisible && _currentActiveHoverRow != null)
        {
            UpdateCardPosition(_lastPointerPosition);
        }
    }

    private void UpdateCardPosition(Windows.Foundation.Point mousePos)
    {
        double cardWidth = HoverPreviewCard.Width > 0 ? HoverPreviewCard.Width : 360;
        double cardHeight = HoverPreviewCard.Height > 0 ? HoverPreviewCard.Height : 240;

        const double margin = 16;
        const double cursorOffset = 18;

        if (XamlRoot?.Content is UIElement rootContent)
        {
            try
            {
                var transformToRoot = TransformToVisual(rootContent);
                var gridOrigin = transformToRoot.TransformPoint(new Windows.Foundation.Point(0, 0));

                // Absolute mouse coordinates in application window space.
                double winMouseX = gridOrigin.X + mousePos.X;
                double winMouseY = gridOrigin.Y + mousePos.Y;

                double windowWidth = XamlRoot.Size.Width;
                double windowHeight = XamlRoot.Size.Height;

                // Position card to the right of the cursor by default.
                double winCardX = winMouseX + cursorOffset;

                // Flip to the left side if exceeding the right boundary of the window.
                if (windowWidth > 0 && winCardX + cardWidth > windowWidth - margin)
                {
                    winCardX = winMouseX - cardWidth - cursorOffset;
                }

                // Pin at horizontal window boundaries: stop moving outward once touching the edge.
                if (winCardX < margin)
                {
                    winCardX = margin;
                }
                else if (windowWidth > 0 && winCardX + cardWidth > windowWidth - margin)
                {
                    winCardX = Math.Max(margin, windowWidth - cardWidth - margin);
                }

                // Position card vertically centered relative to cursor.
                double winCardY = winMouseY - (cardHeight / 2.0);

                // Pin at vertical window boundaries: stop moving downward/upward once touching the edge.
                if (windowHeight > 0 && winCardY + cardHeight > windowHeight - margin)
                {
                    winCardY = Math.Max(margin, windowHeight - cardHeight - margin);
                }
                if (winCardY < margin)
                {
                    winCardY = margin;
                }

                // Translate window coordinates to Popup offset relative to ModGrid origin.
                HoverPreviewPopup.HorizontalOffset = winCardX - gridOrigin.X;
                HoverPreviewPopup.VerticalOffset = winCardY - gridOrigin.Y;
                HoverPreviewTransform.X = 0;
                HoverPreviewTransform.Y = 0;
                return;
            }
            catch (Exception ex)
            {
                Serilog.Log.ForContext<ModGrid>().Warning(ex, "Failed to calculate window-relative hover preview position");
            }
        }

        double fallbackX = mousePos.X + cursorOffset;
        double fallbackY = mousePos.Y - (cardHeight / 2.0);
        HoverPreviewPopup.HorizontalOffset = fallbackX;
        HoverPreviewPopup.VerticalOffset = fallbackY;
    }

    public void HideHoverPreview()
    {
        _hoverTimer.Stop();
        _leaveGraceTimer.Stop();
        _pendingHoverRow = null;
        _currentActiveHoverRow = null;
        _isHoverVisible = false;
        _lastHitSource = null;
        _lastHitRowVm = null;

        if (HoverPreviewPopup != null)
        {
            HoverPreviewPopup.IsOpen = false;
        }
        if (HoverPreviewImage != null)
        {
            HoverPreviewImage.Source = null;
        }
        if (HoverPreviewPlaceholder != null)
        {
            HoverPreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
    }

    private (bool isInteractive, ModRowVM? rowVm) ResolveHitTest(DependencyObject source)
    {
        if (ReferenceEquals(source, _lastHitSource))
        {
            return (_lastHitIsInteractive, _lastHitRowVm);
        }

        _lastHitSource = source;
        _lastHitIsInteractive = IsInteractiveControl(source);
        _lastHitRowVm = _lastHitIsInteractive ? null : FindModRowVM(source);
        return (_lastHitIsInteractive, _lastHitRowVm);
    }

    private static bool IsInteractiveControl(DependencyObject dep)
    {
        var current = dep;
        while (current != null && current is not CommunityToolkit.WinUI.UI.Controls.DataGrid)
        {
            if (current is ButtonBase || current is TextBox || current is ScrollBar)
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private static ModRowVM? FindModRowVM(DependencyObject dep)
    {
        var current = dep;
        while (current != null && current is not CommunityToolkit.WinUI.UI.Controls.DataGrid)
        {
            if (current is FrameworkElement { DataContext: ModRowVM rowVm })
                return rowVm;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    #endregion
}