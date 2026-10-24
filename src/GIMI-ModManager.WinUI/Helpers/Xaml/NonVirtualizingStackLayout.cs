using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GIMI_ModManager.WinUI.Helpers.Xaml;

/// <summary>
/// A non-virtualizing vertical stack layout for ItemsRepeater.
/// Used inside nested Expander containers to guarantee 100% immediate rendering on expansion (no blank area)
/// and immutable extent height during over-scroll bounces (eliminating infinite bouncing loops).
/// </summary>
public sealed partial class NonVirtualizingStackLayout : NonVirtualizingLayout
{
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing),
            typeof(double),
            typeof(NonVirtualizingStackLayout),
            new PropertyMetadata(0.0, (d, _) => ((NonVirtualizingStackLayout)d).InvalidateMeasure()));

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var children = context.Children;
        var count = children.Count;
        double totalHeight = 0;
        double maxWidth = 0;
        var childAvailableSize = new Size(availableSize.Width, double.PositiveInfinity);

        for (int i = 0; i < count; i++)
        {
            var element = children[i];
            element.Measure(childAvailableSize);
            var desiredSize = element.DesiredSize;

            totalHeight += desiredSize.Height;
            if (desiredSize.Width > maxWidth)
            {
                maxWidth = desiredSize.Width;
            }

            if (i < count - 1)
            {
                totalHeight += Spacing;
            }
        }

        var finalWidth = double.IsInfinity(availableSize.Width) ? maxWidth : Math.Max(maxWidth, availableSize.Width);
        return new Size(finalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        var count = children.Count;
        double currentY = 0;

        for (int i = 0; i < count; i++)
        {
            var element = children[i];
            var desiredSize = element.DesiredSize;
            var width = finalSize.Width;

            element.Arrange(new Rect(0, currentY, width, desiredSize.Height));
            currentY += desiredSize.Height;

            if (i < count - 1)
            {
                currentY += Spacing;
            }
        }

        return new Size(finalSize.Width, Math.Max(finalSize.Height, currentY));
    }
}