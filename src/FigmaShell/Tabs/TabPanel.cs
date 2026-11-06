using System.Windows;
using System.Windows.Controls;

namespace FigmaShell.Tabs;

/// <summary>
/// Lays tabs out left-to-right. Pinned (icon-only) tabs get a fixed width; file tabs share the rest
/// equally and shrink (Chrome-style) as more are opened, leaving <see cref="ReservedWidth"/> free for
/// the "+" button and a window-drag area.
/// </summary>
public sealed class TabPanel : Panel
{
    public double PinnedTabWidth { get; set; } = 40;
    public double MaxTabWidth { get; set; } = 220;
    public double MinTabWidth { get; set; } = 56;
    public double ReservedWidth { get; set; } = 96;

    private double _tabWidth;

    private static bool IsPinned(UIElement child) =>
        (child as FrameworkElement)?.DataContext is TabItemModel { IsPinned: true };

    protected override Size MeasureOverride(Size availableSize)
    {
        int pinned = 0, files = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (IsPinned(child)) pinned++; else files++;
        }
        if (pinned + files == 0) return new Size(0, 0);

        double pinnedWidth = pinned * PinnedTabWidth;
        double available = double.IsInfinity(availableSize.Width)
            ? files * MaxTabWidth
            : Math.Max(0, availableSize.Width - ReservedWidth - pinnedWidth);

        _tabWidth = files == 0 ? 0 : Math.Clamp(available / files, MinTabWidth, MaxTabWidth);

        double height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(IsPinned(child) ? PinnedTabWidth : _tabWidth, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }

        double width = pinnedWidth + _tabWidth * files;
        if (!double.IsInfinity(availableSize.Width)) width = Math.Min(width, availableSize.Width);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        foreach (UIElement child in InternalChildren)
        {
            double w = IsPinned(child) ? PinnedTabWidth : _tabWidth;
            child.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w;
        }
        return finalSize;
    }
}
