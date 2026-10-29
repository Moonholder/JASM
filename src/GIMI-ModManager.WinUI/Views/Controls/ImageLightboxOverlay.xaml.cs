using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using VirtualKey = Windows.System.VirtualKey;

namespace GIMI_ModManager.WinUI.Views.Controls;

public sealed partial class ImageLightboxOverlay : UserControl
{
    private XamlRoot? _activeXamlRoot;

    public ImageLightboxOverlay()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    public bool IsOpen => LightboxPopup.IsOpen;

    public void Show(Uri? imageUri)
    {
        if (imageUri == null)
            return;

        try
        {
            var bitmap = new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                UriSource = imageUri
            };
            Show(bitmap);
        }
        catch
        {
            // Invalid image URI or decoding failure
        }
    }

    public void Show(ImageSource? imageSource)
    {
        if (imageSource == null)
            return;

        LightboxImage.Source = imageSource;

        if (XamlRoot != null && XamlRoot.Content != null)
        {
            AttachXamlRoot(XamlRoot);
            LightboxPopup.XamlRoot = XamlRoot;
            SynchronizeLayout();
        }

        LightboxPopup.IsOpen = true;
        LightboxGrid.Focus(FocusState.Programmatic);

        // Schedule an additional layout synchronization to ensure correct alignment after the next WinUI layout cycle
        DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, SynchronizeLayout);
    }

    public void Close()
    {
        if (!LightboxPopup.IsOpen)
            return;

        LightboxPopup.IsOpen = false;
        DetachXamlRoot();
        LightboxImage.Source = null;
    }

    private void AttachXamlRoot(XamlRoot root)
    {
        if (ReferenceEquals(_activeXamlRoot, root))
            return;

        DetachXamlRoot();
        _activeXamlRoot = root;
        _activeXamlRoot.Changed += OnXamlRootChanged;
    }

    private void DetachXamlRoot()
    {
        if (_activeXamlRoot != null)
        {
            _activeXamlRoot.Changed -= OnXamlRootChanged;
            _activeXamlRoot = null;
        }
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (!LightboxPopup.IsOpen)
            return;

        SynchronizeLayout();
        DispatcherQueue?.TryEnqueue(DispatcherQueuePriority.Low, SynchronizeLayout);
    }

    private void SynchronizeLayout()
    {
        if (!LightboxPopup.IsOpen || _activeXamlRoot?.Content == null)
            return;

        var windowSize = _activeXamlRoot.Size;
        LightboxGrid.Width = windowSize.Width;
        LightboxGrid.Height = windowSize.Height;

        try
        {
            var transform = TransformToVisual(_activeXamlRoot.Content);
            var localOrigin = transform.TransformPoint(new Point(0, 0));
            LightboxPopup.HorizontalOffset = -localOrigin.X;
            LightboxPopup.VerticalOffset = -localOrigin.Y;
        }
        catch
        {
            // Transform might fail if visual tree is reorganizing during fullscreen transition
        }
    }

    private void OnOverlayTapped(object sender, TappedRoutedEventArgs e)
    {
        Close();
    }

    private void OnGridKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Close();
    }
}