using System.Collections.Specialized;
using GIMI_ModManager.Core.Services.GameBanana.Models;
using GIMI_ModManager.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace GIMI_ModManager.WinUI.Views;

public sealed partial class GameBananaPage : Page
{
    public GameBananaVM ViewModel { get; } = App.GetService<GameBananaVM>();

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

        if (_gridScrollViewer != null)
        {
            _gridScrollViewer.ViewChanged -= InternalScrollViewer_ViewChanged;
        }

        try
        {
            DescriptionWebView?.Close();
        }
        catch { }

        try
        {
            UpdateLogWebView?.Close();
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
            _viewportFillTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _viewportFillTimer.Tick += (_, _) =>
            {
                _viewportFillTimer.Stop();
                ProcessScrollPosition();
            };
            _viewportFillTimer.Start();
        }
    }

    private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.SelectedModDescription))
        {
            if (DetailSegmented != null) DetailSegmented.SelectedIndex = 0;
            await NavigateWebView(DescriptionWebView, ViewModel.SelectedModDescription);
        }
        else if (e.PropertyName == nameof(ViewModel.SelectedModUpdateLog))
        {
            await NavigateWebView(UpdateLogWebView, ViewModel.SelectedModUpdateLog);
        }
    }

    private void DetailSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TabOverview == null || TabDescription == null || TabUpdateLog == null) return;

        TabOverview.Visibility = Visibility.Collapsed;
        TabDescription.Visibility = Visibility.Collapsed;
        TabUpdateLog.Visibility = Visibility.Collapsed;

        // Also explicitly collapse WebViews to prevent WinUI 3 visual overlapping bugs
        DescriptionWebView?.Visibility = Visibility.Collapsed;
        if (UpdateLogWebView != null) UpdateLogWebView.Visibility = Visibility.Collapsed;

        switch (DetailSegmented.SelectedIndex)
        {
            case 0:
                TabOverview.Visibility = Visibility.Visible;
                break;
            case 1:
                TabDescription.Visibility = Visibility.Visible;
                if (DescriptionWebView != null) DescriptionWebView.Visibility = Visibility.Visible;
                break;
            case 2:
                TabUpdateLog.Visibility = Visibility.Visible;
                if (UpdateLogWebView != null) UpdateLogWebView.Visibility = Visibility.Visible;
                break;
        }
    }

    private static async Task NavigateWebView(WebView2 webView, string? htmlContent)
    {
        try
        {
            await webView.EnsureCoreWebView2Async();
            var themeService = App.GetService<Contracts.Services.IThemeSelectorService>();
            bool isDark = themeService.Theme == ElementTheme.Dark;
            if (themeService.Theme == ElementTheme.Default)
            {
                isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            }

            webView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;

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

            webView.NavigateToString(wrappedHtml);
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

    private void PreviewImage_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid && grid.Children.Count > 1 && grid.Children[1] is Border overlay)
        {
            overlay.Opacity = 1;
        }
    }

    private void PreviewImage_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid && grid.Children.Count > 1 && grid.Children[1] is Border overlay)
        {
            overlay.Opacity = 0;
        }
    }

    private void PreviewImage_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is GbPreviewImageItem imageItem)
        {
            LightBoxOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            LightBoxLoading.IsActive = true;
            LightBoxLoading.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
            LightBoxImage.Source = null;

            // Load high res image
            Helpers.RemoteImageLoader.LoadInto(imageItem.Url, 1920, img =>
            {
                if (LightBoxOverlay.Visibility == Microsoft.UI.Xaml.Visibility.Visible)
                {
                    LightBoxImage.Source = img;
                    LightBoxLoading.IsActive = false;
                    LightBoxLoading.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                }
            }, "_1920");
        }
    }

    private void LightBoxOverlay_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource == LightBoxOverlay)
        {
            CloseLightBox();
        }
    }

    private void LightBoxClose_Click(object sender, RoutedEventArgs e)
    {
        CloseLightBox();
    }

    private void CloseLightBox()
    {
        LightBoxOverlay.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        LightBoxImage.Source = null;
        LightBoxLoading.IsActive = false;
        LightBoxLoading.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    private void ModCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            grid.Scale = new System.Numerics.Vector3(1.03f, 1.03f, 1f);
        }
    }

    private void ModCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            grid.Scale = new System.Numerics.Vector3(1f, 1f, 1f);
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
}