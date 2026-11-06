using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FigmaShell.Interop;
using FigmaShell.Services;
using FigmaShell.Tabs;
using FigmaShell.Web;
using Microsoft.Web.WebView2.Core;

namespace FigmaShell;

public partial class MainWindow : Window
{
    private const int TitleBarColorRef = 0x001E1E1E; // COLORREF (0x00BBGGRR) of #1E1E1E
    private static readonly Brush CaptionHoverBrush = CreateFrozenBrush(0x1F, 0xFF, 0xFF, 0xFF);

    private readonly TabManager _tabs;
    private TabItemModel? _titleSource;

    private bool _maxHover;
    private bool _maxPressed;

    private bool _fullScreen;
    private WindowState _stateBeforeFullScreen;

    public MainWindow(Task<CoreWebView2Environment> environment, SessionData? session, IReadOnlyList<string> startupUrls)
    {
        InitializeComponent();

        _tabs = new TabManager(ContentHost, environment);
        _tabs.ActiveChanged += OnActiveTabChanged;
        _tabs.FullScreenRequested += SetFullScreen;
        Strip.Manager = _tabs;

        ApplySavedBounds(session);

        _tabs.Restore(session?.Tabs ?? [], session?.ActiveIndex ?? 0);
        foreach (var url in startupUrls) _tabs.Open(url);

        SourceInitialized += OnSourceInitialized;
        StateChanged += (_, _) => UpdateWindowStateVisuals();
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
    }

    /// <summary>Called when a second launch forwarded its arguments to this instance.</summary>
    public void HandleExternalLaunch(string[] args)
    {
        foreach (var url in args.Where(UrlRouter.IsFigmaUrl))
            _tabs.Open(url);

        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        _tabs.FocusActive();
    }

    // ---------------------------------------------------------------- window title

    private void OnActiveTabChanged()
    {
        if (_titleSource is not null) _titleSource.PropertyChanged -= OnActiveTabPropertyChanged;
        _titleSource = _tabs.Active;
        if (_titleSource is not null) _titleSource.PropertyChanged += OnActiveTabPropertyChanged;
        UpdateTitle();
    }

    private void OnActiveTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabItemModel.Title)) UpdateTitle();
    }

    private void UpdateTitle()
    {
        var t = _tabs.Active?.Title;
        Title = string.IsNullOrEmpty(t) || t == "Figma" ? "Figma" : $"{t} – Figma";
    }

    // ---------------------------------------------------------------- keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool ctrl = Native.IsKeyDown(Native.VK_CONTROL);
        bool shift = Native.IsKeyDown(Native.VK_SHIFT);
        bool handled = true;

        // Only keys browsers reserve for themselves are taken, so Figma web never relies on them.
        switch (key)
        {
            case Key.T when ctrl && shift: _tabs.ReopenClosed(); break;
            case Key.T when ctrl: _tabs.Activate(_tabs.Home); break;
            case Key.N when ctrl && !shift: _tabs.NewFile(); break;
            case Key.W when ctrl && !shift: _tabs.CloseActive(); break;
            case Key.F4 when ctrl: _tabs.CloseActive(); break;
            case Key.Tab when ctrl: _tabs.SelectRelative(shift ? -1 : 1); break;
            case Key.PageDown when ctrl && !shift: _tabs.SelectRelative(1); break;
            case Key.PageUp when ctrl && !shift: _tabs.SelectRelative(-1); break;
            case >= Key.D1 and <= Key.D8 when ctrl && !shift: _tabs.SelectIndex(key - Key.D1); break;
            case >= Key.NumPad1 and <= Key.NumPad8 when ctrl && !shift: _tabs.SelectIndex(key - Key.NumPad1); break;
            case Key.D9 or Key.NumPad9 when ctrl && !shift: _tabs.SelectLast(); break;
            case Key.F5: _tabs.ReloadActive(ignoreCache: ctrl || shift); break;
            case Key.F11: SetFullScreen(!_fullScreen); break;
#if DEBUG
            case Key.F12: _tabs.OpenDevTools(); break;
#endif
            default: handled = false; break;
        }

        if (handled) e.Handled = true;
    }

    // ---------------------------------------------------------------- caption buttons & snap layouts

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.ApplyDarkFrame(hwnd, TitleBarColorRef);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        UpdateWindowStateVisuals();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_NCHITTEST:
                if (!_fullScreen && IsOverMaxButton(lParam))
                {
                    SetMaxHover(true);
                    handled = true;
                    return Native.HTMAXBUTTON; // lets Windows 11 show the snap-layouts flyout
                }
                SetMaxHover(false);
                break;

            case Native.WM_NCLBUTTONDOWN when wParam.ToInt32() == Native.HTMAXBUTTON:
                _maxPressed = true;
                handled = true;
                return IntPtr.Zero;

            case Native.WM_NCLBUTTONDBLCLK when wParam.ToInt32() == Native.HTMAXBUTTON:
                handled = true;
                return IntPtr.Zero;

            case Native.WM_NCLBUTTONUP when wParam.ToInt32() == Native.HTMAXBUTTON:
                if (_maxPressed) ToggleMaximize();
                _maxPressed = false;
                handled = true;
                return IntPtr.Zero;

            case Native.WM_NCMOUSELEAVE:
                _maxPressed = false;
                SetMaxHover(false);
                break;
        }
        return IntPtr.Zero;
    }

    private bool IsOverMaxButton(IntPtr lParam)
    {
        if (!MaxButton.IsVisible) return false;
        long lp = lParam.ToInt64();
        var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
        try
        {
            var p = MaxButton.PointFromScreen(screen);
            return p.X >= 0 && p.Y >= 0 && p.X < MaxButton.ActualWidth && p.Y < MaxButton.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void SetMaxHover(bool hover)
    {
        if (_maxHover == hover) return;
        _maxHover = hover;
        MaxButton.Background = hover ? CaptionHoverBrush : Brushes.Transparent;
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void MinButton_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void UpdateWindowStateVisuals()
    {
        bool maximized = WindowState == WindowState.Maximized;
        // A maximized WindowChrome window overhangs the monitor by the frame width; pull content back in.
        Root.Margin = maximized && !_fullScreen ? MaximizedInset() : new Thickness(0);
        MaxButton.Content = maximized ? "" : "";
    }

    private Thickness MaximizedInset()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        uint dpiValue = (uint)Math.Round(96 * dpi.DpiScaleX);
        double padded = Native.GetSystemMetricsForDpi(Native.SM_CXPADDEDBORDER, dpiValue);
        double x = (Native.GetSystemMetricsForDpi(Native.SM_CXSIZEFRAME, dpiValue) + padded) / dpi.DpiScaleX;
        double y = (Native.GetSystemMetricsForDpi(Native.SM_CYSIZEFRAME, dpiValue) + padded) / dpi.DpiScaleY;
        return new Thickness(x, y, x, y);
    }

    // ---------------------------------------------------------------- fullscreen

    private void SetFullScreen(bool on)
    {
        if (on == _fullScreen) return;
        _fullScreen = on;

        if (on)
        {
            _stateBeforeFullScreen = WindowState;
            TitleRow.Height = new GridLength(0);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; // force re-layout
            WindowState = WindowState.Maximized;
        }
        else
        {
            TitleRow.Height = new GridLength(36);
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            WindowState = _stateBeforeFullScreen == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
        }

        UpdateWindowStateVisuals();
        _tabs.FocusActive();
    }

    // ---------------------------------------------------------------- session

    private void ApplySavedBounds(SessionData? s)
    {
        // Default size: fit inside the work area (1440x900 DIPs is taller than a 1080p screen at 125%).
        var work = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Round(work.Width * 0.9));
        Height = Math.Min(Height, Math.Round(work.Height * 0.9));

        if (s is null || s.Width < MinWidth || s.Height < MinHeight) return;

        var bounds = new Rect(s.Left, s.Top, s.Width, s.Height);
        var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                               SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!desktop.IntersectsWith(bounds)) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = Math.Min(s.Width, desktop.Width);
        Height = Math.Min(s.Height, desktop.Height);
        Left = s.Left;
        Top = Math.Max(s.Top, desktop.Top); // never restore with the title bar off-screen
        if (s.Maximized) WindowState = WindowState.Maximized;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var b = RestoreBounds;
        bool maximized = WindowState == WindowState.Maximized && !_fullScreen
                         || _fullScreen && _stateBeforeFullScreen == WindowState.Maximized;

        SessionStore.Save(new SessionData
        {
            Tabs = _tabs.FileTabs.Select(t => t.Url).ToList(), // pinned tabs are always recreated
            ActiveIndex = Math.Max(0, _tabs.ActiveIndex),
            Left = b.Left,
            Top = b.Top,
            Width = b.Width,
            Height = b.Height,
            Maximized = maximized,
        });
    }

    private static Brush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
