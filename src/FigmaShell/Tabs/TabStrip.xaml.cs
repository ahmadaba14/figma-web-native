using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FigmaShell.Web;

namespace FigmaShell.Tabs;

public partial class TabStrip : UserControl
{
    private TabManager? _manager;
    private TabItemModel? _dragTab;
    private Point _dragStart;
    private bool _dragging;

    public TabStrip()
    {
        InitializeComponent();
    }

    public TabManager? Manager
    {
        get => _manager;
        set
        {
            _manager = value;
            Items.ItemsSource = value?.Tabs;
        }
    }

    private static TabItemModel? ModelOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as TabItemModel;

    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_manager is null || ModelOf(sender) is not { } tab) return;

        _manager.Activate(tab);
        e.Handled = true;
        if (tab.IsPinned) return; // pinned tabs can't be dragged

        // Capture on the ItemsControl (not the tab) so capture survives the container being moved.
        _dragTab = tab;
        _dragStart = e.GetPosition(Items);
        _dragging = false;
        Items.CaptureMouse();
        e.Handled = true;
    }

    private void Tab_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _manager is not null && ModelOf(sender) is { } tab)
        {
            _manager.Close(tab);
            e.Handled = true;
        }
    }

    private void Items_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTab is null || _manager is null || !Items.IsMouseCaptured) return;

        var p = e.GetPosition(Items);
        if (!_dragging && Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance) return;
        _dragging = true;

        int target = IndexAt(p.X);
        int current = _manager.Tabs.IndexOf(_dragTab);
        if (current >= 0 && target >= TabManager.PinnedCount && target != current)
            _manager.Move(current, target);
    }

    /// <summary>Index of the tab under x (tabs have mixed widths, so measure the real containers).</summary>
    private int IndexAt(double x)
    {
        int count = Items.Items.Count;
        for (int i = 0; i < count; i++)
        {
            if (Items.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
            double left = c.TranslatePoint(new Point(0, 0), Items).X;
            if (x < left + c.ActualWidth) return i;
        }
        return count - 1;
    }

    private void Items_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool wasDragging = _dragging;
        EndDrag();
        if (!wasDragging) _manager?.FocusActive();
    }

    private void Items_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _dragTab = null;
        _dragging = false;
    }

    private void EndDrag()
    {
        _dragTab = null;
        _dragging = false;
        if (Items.IsMouseCaptured) Items.ReleaseMouseCapture();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_manager is not null && ModelOf(sender) is { } tab)
            _manager.Close(tab);
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)Resources["NewMenu"];
        menu.PlacementTarget = NewTabButton;
        menu.IsOpen = true;
    }

    private void NewDesign_Click(object sender, RoutedEventArgs e) => _manager?.NewFile();
    private void NewBoard_Click(object sender, RoutedEventArgs e) => _manager?.NewBoard();
    private void NewSlides_Click(object sender, RoutedEventArgs e) => _manager?.NewSlides();
    private void NewMake_Click(object sender, RoutedEventArgs e) => _manager?.NewMake();
    private void NewSpec_Click(object sender, RoutedEventArgs e) => _manager?.NewSpec();
    private void NewBuzz_Click(object sender, RoutedEventArgs e) => _manager?.NewBuzz();
    private void NewSite_Click(object sender, RoutedEventArgs e) => _manager?.NewSite();
    private void OpenExisting_Click(object sender, RoutedEventArgs e) => _manager?.BrowseFiles();

    // Hand keyboard focus back to Figma when the menu closes (item picked or Esc).
    private void NewMenu_Closed(object sender, RoutedEventArgs e) => _manager?.FocusActive();
}
