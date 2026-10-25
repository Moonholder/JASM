using System.Collections.Specialized;
using GIMI_ModManager.Core.Services.GameBanana.Models;
using GIMI_ModManager.WinUI.ViewModels;
using GIMI_ModManager.WinUI.Models.GameBanana;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace GIMI_ModManager.WinUI.Views;

public sealed partial class GameBananaPage : Page
{
    public GameBananaVM ViewModel { get; } = App.GetService<GameBananaVM>();

    // Cached per-card border brushes: allocating on every hover event causes
    // avoidable UI-thread garbage.
    private static readonly SolidColorBrush HoverCardBorderBrush =
        new(Microsoft.UI.ColorHelper.FromArgb(0x60, 0x60, 0xCD, 0xFF));
    private static readonly SolidColorBrush DefaultCardBorderBrush =
        new(Microsoft.UI.ColorHelper.FromArgb(0x25, 0xFF, 0xFF, 0xFF));

    public GameBananaPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Mods.CollectionChanged += Mods_CollectionChanged;
        Unloaded += Page_Unloaded;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Mods.CollectionChanged -= Mods_CollectionChanged;

        _viewportFillTimer?.Stop();
        _scrollThrottleTimer?.Stop();
        ModsGridView.SizeChanged -= ModsGridView_SizeChanged;

        if (_gridScrollViewer != null)
        {
            _gridScrollViewer.ViewChanged -= InternalScrollViewer_ViewChanged;
        }

        try
        {
            if (_detailWebView != null)
            {
                _detailWebView.NavigationStarting -= WebView_NavigationStarting;
                _detailWebView.Close();
                _detailWebView = null;
            }
        }
        catch { }

        Bindings.StopTracking();
    }

    private DispatcherTimer? _viewportFillTimer;

    private void Mods_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
        {
            // Debounce: wait for layout to settle, then check if viewport needs more content
            _viewportFillTimer?.Stop();
            _viewportFillTimer ??= CreateViewportFillTimer();
            _viewportFillTimer.Start();
        }
    }

    private DispatcherTimer CreateViewportFillTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += ViewportFill_Tick;
        return timer;
    }

    private void ViewportFill_Tick(object? sender, object e)
    {
        _viewportFillTimer?.Stop();
        ProcessScrollPosition();
    }

    private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.SelectedModDescription))
        {
            if (DetailSegmented != null) DetailSegmented.SelectedIndex = 0;
        }
        else if (e.PropertyName == nameof(ViewModel.SelectedModUpdateLog))
        {
            // The webview is created lazily; if the Updates tab is already
            // visible, refresh it in place.
            if (DetailSegmented?.SelectedIndex == 2)
            {
                await ShowDetailWebViewAsync(TabUpdateLog, ViewModel.SelectedModUpdateLog);
            }
        }
        else if (e.PropertyName == nameof(ViewModel.IsDetailPaneOpen) && !ViewModel.IsDetailPaneOpen)
        {
            try
            {
                _detailWebView?.NavigateToString("");
                _lastRenderedHtml = "";
            }
            catch { }
        }
    }

    private async void DetailSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabOverview == null || TabDescription == null || TabUpdateLog == null) return;

        TabOverview.Visibility = Visibility.Collapsed;
        TabDescription.Visibility = Visibility.Collapsed;
        TabUpdateLog.Visibility = Visibility.Collapsed;

        switch (DetailSegmented.SelectedIndex)
        {
            case 0:
                TabOverview.Visibility = Visibility.Visible;
                DetachDetailWebView();
                break;
            case 1:
                TabDescription.Visibility = Visibility.Visible;
                await ShowDetailWebViewAsync(TabDescription, ViewModel.SelectedModDescription);
                break;
            case 2:
                TabUpdateLog.Visibility = Visibility.Visible;
                await ShowDetailWebViewAsync(TabUpdateLog, ViewModel.SelectedModUpdateLog);
                break;
        }
    }

    private WebView2? _detailWebView;
    private Border? _webViewHost;
    private int _webViewRequestVersion;
    private string? _lastRenderedHtml;

    private void DetachDetailWebView()
    {
        _webViewRequestVersion++;
        _webViewHost?.Child = null;
        _webViewHost = null;
    }

    /// <summary>
    /// Attaches the single shared WebView2 to the given tab host (creating it
    /// on first use) and renders the content. Scripts are disabled because the
    /// HTML comes from third-party mod authors.
    /// </summary>
    private async Task ShowDetailWebViewAsync(Border host, string? htmlContent)
    {
        var version = ++_webViewRequestVersion;

        try
        {
            if (_detailWebView == null)
            {
                _detailWebView = new WebView2
                {
                    DefaultBackgroundColor = Microsoft.UI.Colors.Transparent
                };
                _detailWebView.NavigationStarting += WebView_NavigationStarting;
            }

            if (!ReferenceEquals(_webViewHost, host))
            {
                _webViewHost?.Child = null;
                host.Child = _detailWebView;
                _webViewHost = host;
            }

            await _detailWebView.EnsureCoreWebView2Async();

            // A newer tab switch happened while we were initializing; abort so a
            // stale request cannot re-attach the webview to an inactive tab.
            if (version != _webViewRequestVersion)
                return;

            if (_detailWebView.CoreWebView2 is { } core)
            {
                core.Settings.IsScriptEnabled = false;
                core.Settings.AreDefaultScriptDialogsEnabled = false;
                core.Settings.IsWebMessageEnabled = false;
            }

            var themeService = App.GetService<Contracts.Services.IThemeSelectorService>();
            bool isDark = themeService.Theme == ElementTheme.Dark;
            if (themeService.Theme == ElementTheme.Default)
            {
                isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            }

            _detailWebView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;

            var textColor = isDark ? "#ffffff" : "#000000";
            var linkColor = isDark ? "#60CDFF" : "#005FB8";
            var codeBg = isDark ? "#2d2d2d" : "#f5f5f5";
            var quoteColor = isDark ? "#aaaaaa" : "#666666";
            var quoteBorder = isDark ? "#555555" : "#cccccc";

            var wrappedHtml = $$"""
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset="utf-8" />
                    <style>
                        body, p, h1, h2, h3, h4, h5, h6, li {
                            color: {{textColor}} !important;
                        }
                        div, span, td, th {
                            color: {{textColor}} !important;
                            background-color: transparent !important;
                        }
                        html, body {
                            background-color: transparent !important;
                            font-family: 'Segoe UI Variable', 'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, sans-serif;
                            font-size: 14px;
                            line-height: 1.5;
                            word-wrap: break-word;
                            padding: 8px 12px;
                            margin: 0;
                        }
                        ::-webkit-scrollbar {
                            width: 14px;
                            height: 14px;
                        }
                        ::-webkit-scrollbar-track {
                            background: transparent;
                        }
                        ::-webkit-scrollbar-thumb {
                            background-color: rgba(128, 128, 128, 0.4);
                            background-clip: padding-box;
                            border: 4px solid rgba(0, 0, 0, 0);
                            border-radius: 8px;
                        }
                        ::-webkit-scrollbar-thumb:hover {
                            background-color: rgba(128, 128, 128, 0.6);
                        }
                        a, a span { color: {{linkColor}} !important; text-decoration: none; }
                        a:hover { text-decoration: underline; }
                        img, iframe, video { max-width: 100%; height: auto; border-radius: 4px; }
                        pre, code { background-color: {{codeBg}} !important; border-radius: 4px; padding: 2px 4px; font-family: 'Consolas', monospace; color: {{textColor}} !important; }
                        pre { padding: 12px; overflow-x: auto; }
                        pre code { padding: 0; background-color: transparent !important; }
                        blockquote { border-left: 4px solid {{quoteBorder}}; padding-left: 12px; margin-left: 0; color: {{quoteColor}} !important; }
                        
                        /* Scrollbar styling */
                        ::-webkit-scrollbar { width: 8px; height: 8px; }
                        ::-webkit-scrollbar-track { background: transparent; }
                        ::-webkit-scrollbar-thumb { background: {{quoteBorder}}; border-radius: 4px; }
                        ::-webkit-scrollbar-thumb:hover { background: {{quoteColor}}; }
                    </style>
                </head>
                <body>
                    {{htmlContent ?? ""}}
                </body>
                </html>
                """;

            // Skip re-navigating when the same content is already rendered
            // (e.g. switching back to the tab that was just shown).
            if (!string.Equals(_lastRenderedHtml, wrappedHtml, StringComparison.Ordinal))
            {
                _detailWebView.NavigateToString(wrappedHtml);
                _lastRenderedHtml = wrappedHtml;
            }
        }
        catch
        {
            // WebView2 may not be available
        }
    }

    private void WebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // Block external navigation — open links in the system browser instead
        if (args.Uri != null && !args.Uri.StartsWith("about:blank") && !args.Uri.StartsWith("data:"))
        {
            args.Cancel = true;
            try { _ = Windows.System.Launcher.LaunchUriAsync(new Uri(args.Uri)); }
            catch { /* ignore */ }
        }
    }

    private void ModsGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GbModDisplayItem mod)
        {
            ViewModel.OpenModDetailCommand.Execute(mod);
        }
    }

    private void ModsGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            // A recycled container is far off-screen: drop its bitmaps so the
            // collection does not keep decoded images alive indefinitely.
            if (args.ItemContainer is GridViewItem recycledContainer)
            {
                recycledContainer.SizeChanged -= CardContainer_SizeChanged;
            }
            if (args.Item is GbModDisplayItem recycled)
            {
                recycled.ReleaseImages();
            }
            return;
        }

        // Lazy-load the thumbnail only when the card is actually realized.
        if (args.Item is GbModDisplayItem { ThumbnailImage: null } item)
        {
            item.LoadThumbnail();
        }

        // Size the cover area to the exact 16:9 aspect of the realized card
        // width, so UniformToFill shows the whole image without cropping the
        // top/bottom (GameBanana renders covers in a precise 16:9 box).
        if (args.ItemContainer is GridViewItem container)
        {
            container.SizeChanged -= CardContainer_SizeChanged;
            container.SizeChanged += CardContainer_SizeChanged;
        }
    }

    private void CardContainer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not GridViewItem container || e.NewSize.Width <= 0)
            return;

        if (FindChildByName<Grid>(container, "CardCoverArea") is { } coverArea)
        {
            const double coverAspect = 16.0 / 9.0;
            coverArea.Height = Math.Max(120, Math.Round((e.NewSize.Width - 14) / coverAspect));
        }
    }

    private void ModsGridView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter && e.Key != Windows.System.VirtualKey.Space)
            return;

        if (FocusManager.GetFocusedElement() is GridViewItem item &&
            item.Content is GbModDisplayItem mod)
        {
            ViewModel.OpenModDetailCommand.Execute(mod);
            e.Handled = true;
        }
    }

    private ScrollViewer? _gridScrollViewer;
    private DispatcherTimer? _scrollThrottleTimer;
    private bool _scrollDirty;

    /// <summary>
    /// When the GridView is loaded, subscribe to SizeChanged so we can find the
    /// internal ScrollViewer once the GridView is actually visible and sized.
    /// </summary>
    private void ModsGridView_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not GridView gridView) return;

        // Try immediately (in case visual tree is already ready)
        TryAttachScrollViewer(gridView);

        // Also subscribe to SizeChanged as a reliable fallback —
        // this fires when the GridView goes from Collapsed → Visible and lays out content.
        gridView.SizeChanged += ModsGridView_SizeChanged;
    }

    private void ModsGridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_gridScrollViewer == null && sender is GridView gridView && e.NewSize.Height > 0)
        {
            TryAttachScrollViewer(gridView);
        }

        // After items are rendered, check if content fills the viewport.
        // If not (scrollableHeight == 0), this will trigger loading more.
        ProcessScrollPosition();
    }

    private void TryAttachScrollViewer(GridView gridView)
    {
        if (_gridScrollViewer != null) return;

        var sv = FindChild<ScrollViewer>(gridView);
        if (sv == null) return;

        _gridScrollViewer = sv;
        sv.ViewChanged += InternalScrollViewer_ViewChanged;

        // Set up throttle timer (fires every 100ms while scrolling)
        _scrollThrottleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _scrollThrottleTimer.Tick += ScrollThrottle_Tick;
    }

    private void InternalScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        // Mark scroll as dirty; the throttle timer will pick it up
        _scrollDirty = true;

        // Ensure the throttle timer is running
        if (_scrollThrottleTimer is { IsEnabled: false })
            _scrollThrottleTimer.Start();

        // When scrolling stops, do a final check
        if (!e.IsIntermediate)
        {
            ProcessScrollPosition();
            _scrollThrottleTimer?.Stop();
        }
    }

    private void ScrollThrottle_Tick(object? sender, object e)
    {
        if (_scrollDirty)
        {
            _scrollDirty = false;
            ProcessScrollPosition();
        }
        else
        {
            // No scroll activity; stop the timer to save resources
            _scrollThrottleTimer?.Stop();
        }
    }

    private void ProcessScrollPosition()
    {
        if (_gridScrollViewer is not { } sv) return;

        ViewModel.NotifyScrollPosition(
            sv.VerticalOffset,
            sv.ScrollableHeight,
            sv.ViewportHeight);
    }

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is GbCategoryDisplayItem category)
        {
            ViewModel.SelectCategoryCommand.Execute(category);
        }
    }

    private void CategoryList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.Item is GbCategoryDisplayItem item && item.IconImage == null)
        {
            item.LoadIcon();
        }
    }

    private void PreviewListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.Item is GbPreviewImageItem item && item.ImageSource == null)
        {
            item.Load();
        }
    }

    private void PreviewThumb_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid) grid.Opacity = 1.0;
    }

    private void PreviewThumb_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid) grid.Opacity = 0.7;
    }

    private void MainPreviewOverlay_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (MainPreviewHoverOverlay != null) MainPreviewHoverOverlay.Opacity = 1;
    }

    private void MainPreviewOverlay_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (MainPreviewHoverOverlay != null) MainPreviewHoverOverlay.Opacity = 0;
    }

    private void PreviewThumbnail_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GbPreviewImageItem imageItem)
        {
            var index = ViewModel.PreviewImages.IndexOf(imageItem);
            ViewModel.SelectPreviewImage(index);

            // Open full-screen LightBox
            if (ViewModel.LightBoxSelectedIndex == index)
                ViewModel.ForceLoadLightBoxImages(index);
            else
                ViewModel.LightBoxSelectedIndex = index;

            LightBoxOverlay.Visibility = Visibility.Visible;
            LightBoxFlipView.Focus(FocusState.Programmatic);
        }
    }

    private void MainPreview_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.PreviewImages.Count > 0)
        {
            // Force reload if index did not change
            if (ViewModel.LightBoxSelectedIndex == ViewModel.SelectedPreviewIndex)
            {
                ViewModel.ForceLoadLightBoxImages(ViewModel.LightBoxSelectedIndex);
            }
            else
            {
                ViewModel.LightBoxSelectedIndex = ViewModel.SelectedPreviewIndex;
            }

            LightBoxOverlay.Visibility = Visibility.Visible;
            LightBoxFlipView.Focus(FocusState.Programmatic);
        }
    }

    private void LightBoxOverlay_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Dismiss when clicking the outer backdrop
        if (ReferenceEquals(e.OriginalSource, LightBoxOverlay))
        {
            CloseLightBox();
        }
    }

    private void LightBoxItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Dismiss when tapping outside the image
        if (e.OriginalSource is Image) return;
        CloseLightBox();
    }

    private void LightBoxClose_Click(object sender, RoutedEventArgs e)
    {
        CloseLightBox();
    }

    private void CloseLightBox()
    {
        LightBoxOverlay.Visibility = Visibility.Collapsed;
    }

    private void ModCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.TranslationTransition ??= new Vector3Transition { Duration = TimeSpan.FromMilliseconds(120) };
            element.Translation = new System.Numerics.Vector3(0f, -3f, 0f);
        }
        if (sender is Border border)
        {
            border.BorderBrush = HoverCardBorderBrush;
        }
    }

    private void ModCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.Translation = new System.Numerics.Vector3(0f, 0f, 0f);
        }
        if (sender is Border border)
        {
            border.BorderBrush = DefaultCardBorderBrush;
        }
    }

    private void ModCard_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.CenterPoint = new System.Numerics.Vector3((float)(element.RenderSize.Width / 2), (float)(element.RenderSize.Height / 2), 0f);
            element.ScaleTransition ??= new Vector3Transition { Duration = TimeSpan.FromMilliseconds(100) };
            element.Scale = new System.Numerics.Vector3(0.98f, 0.98f, 1f);
            element.Translation = new System.Numerics.Vector3(0f, 0f, 0f);
        }
    }

    private void ModCard_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.Scale = new System.Numerics.Vector3(1f, 1f, 1f);
            element.Translation = new System.Numerics.Vector3(0f, -3f, 0f);
        }
    }

    private void ModCard_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            element.Scale = new System.Numerics.Vector3(1f, 1f, 1f);
            element.Translation = new System.Numerics.Vector3(0f, 0f, 0f);
        }
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ModFileInfo fileInfo })
        {
            ViewModel.DownloadAndInstallCommand.Execute(fileInfo);
        }
    }

    private void CancelDownloadTask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GbDownloadTask task)
        {
            ViewModel.CancelDownloadTaskCommand.Execute(task);
        }
    }

    private void RemoveDownloadTask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GbDownloadTask task)
        {
            ViewModel.RemoveDownloadTaskCommand.Execute(task);
        }
    }

    private void RetryDownloadTask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GbDownloadTask task)
        {
            ViewModel.RetryDownloadTaskCommand.Execute(task);
        }
    }

    /// <summary>
    /// Recursively find a child element of the specified type in the visual tree.
    /// </summary>
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                return typed;
            var found = FindChild<T>(child);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Recursively find a named child element of the specified type in the visual tree.
    /// </summary>
    private static T? FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && typed.Name == name)
                return typed;
            var found = FindChildByName<T>(child, name);
            if (found != null)
                return found;
        }
        return null;
    }
}