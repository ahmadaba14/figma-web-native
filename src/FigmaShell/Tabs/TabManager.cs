using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FigmaShell.Interop;
using FigmaShell.Web;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FigmaShell.Tabs;

/// <summary>
/// Owns the tab list and one WebView2 per tab. Only the active tab's WebView is visible; the rest are
/// collapsed (so Chromium throttles them), hinted to use less memory, and suspended once idle.
/// </summary>
public sealed class TabManager
{
    /// <summary>Background tabs idle this long get their renderer suspended (instant resume on click).</summary>
    public static TimeSpan SuspendAfter { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Background tabs idle this long get their WebView disposed (reloads on click). Null = never.</summary>
    public static TimeSpan? DiscardAfter { get; set; } = null;

    private static readonly System.Drawing.Color PageBackground = System.Drawing.Color.FromArgb(255, 0x2C, 0x2C, 0x2C);

    private readonly Grid _host;
    private readonly Task<CoreWebView2Environment> _environment;
    private readonly Stack<string> _closed = new();
    private readonly DispatcherTimer _idleTimer;
    private bool _runtimeErrorShown;

    public TabManager(Grid host, Task<CoreWebView2Environment> environment)
    {
        _host = host;
        _environment = environment;
        _idleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
        _idleTimer.Tick += async (_, _) => await SuspendIdleTabsAsync();
        _idleTimer.Start();

        Home = new TabItemModel(UrlRouter.HomeUrl, TabKind.Home) { Title = "Home", IsSuspended = true };
        Community = new TabItemModel(UrlRouter.CommunityUrl, TabKind.Community) { Title = "Community", IsSuspended = true };
        Tabs.Add(Home);
        Tabs.Add(Community);
    }

    public ObservableCollection<TabItemModel> Tabs { get; } = [];
    public TabItemModel? Active { get; private set; }

    /// <summary>Permanent icon-only tabs, always at index 0 and 1.</summary>
    public TabItemModel Home { get; }
    public TabItemModel Community { get; }
    public const int PinnedCount = 2;

    public IEnumerable<TabItemModel> FileTabs => Tabs.Where(t => !t.IsPinned);

    public event Action? ActiveChanged;
    /// <summary>The active page entered/left HTML fullscreen (e.g. prototype presentation).</summary>
    public event Action<bool>? FullScreenRequested;

    // ---------------------------------------------------------------- tab lifecycle

    public TabItemModel Open(string url, bool activate = true, int? index = null)
    {
        // Home / Community URLs belong in the pinned tabs, never in a file tab.
        switch (UrlRouter.Classify(url))
        {
            case FigmaArea.Home: return ShowPinned(Home, url, activate);
            case FigmaArea.Community: return ShowPinned(Community, url, activate);
        }

        // One click can reach us via window.open, NavigationStarting and a pushState SourceChanged.
        // Collapse them: the same file key (or the same "new" URL within a moment) reuses the tab.
        var key = UrlRouter.GetFileKey(url);
        TabItemModel? existing = key is not null
            ? FileTabs.FirstOrDefault(t => string.Equals(UrlRouter.GetFileKey(t.Url), key, StringComparison.Ordinal))
            : (_lastOpened is not null && Tabs.Contains(_lastOpened) &&
               string.Equals(_lastOpenedUrl, url, StringComparison.OrdinalIgnoreCase) &&
               DateTime.UtcNow - _lastOpenedAt < TimeSpan.FromMilliseconds(1500))
                ? _lastOpened : null;
        if (existing is not null)
        {
            if (activate) Activate(existing);
            return existing;
        }

        var tab = new TabItemModel(url);
        _lastOpened = tab;
        _lastOpenedUrl = url;
        _lastOpenedAt = DateTime.UtcNow;
        int i = Math.Clamp(index ?? Tabs.Count, PinnedCount, Tabs.Count);
        Tabs.Insert(i, tab);
        if (activate) Activate(tab);
        return tab;
    }

    private TabItemModel? _lastOpened;
    private string? _lastOpenedUrl;
    private DateTime _lastOpenedAt;

    /// <summary>Create a new design file / FigJam board / Slides deck / etc. in a new tab.</summary>
    public TabItemModel NewFile() => Open(UrlRouter.NewFileUrl);
    public TabItemModel NewBoard() => Open(UrlRouter.NewBoardUrl);
    public TabItemModel NewSlides() => Open(UrlRouter.NewSlidesUrl);
    public TabItemModel NewMake() => Open(UrlRouter.NewMakeUrl);
    public TabItemModel NewSpec() => Open(UrlRouter.NewSpecUrl);
    public TabItemModel NewBuzz() => Open(UrlRouter.NewBuzzUrl);
    public TabItemModel NewSite() => Open(UrlRouter.NewSiteUrl);

    /// <summary>"Open existing file": browse in the Home tab; picked files open in their own tab.</summary>
    public void BrowseFiles() => Activate(Home);

    /// <summary>Recreate file tabs from a saved session without loading any of them except the active one.</summary>
    public void Restore(IReadOnlyList<string> urls, int activeIndex)
    {
        foreach (var url in urls)
        {
            if (!UrlRouter.IsFigmaUrl(url)) continue;
            if (UrlRouter.Classify(url) is FigmaArea.Home or FigmaArea.Community) continue;
            Tabs.Add(new TabItemModel(url) { IsSuspended = true });
        }
        Activate(Tabs[Math.Clamp(activeIndex, 0, Tabs.Count - 1)]);
    }

    /// <summary>Bring up a pinned tab, navigating it to <paramref name="url"/> if it is elsewhere.</summary>
    private TabItemModel ShowPinned(TabItemModel pinned, string url, bool activate = true)
    {
        // A bare /files or /community ("Back to files", logo click) must not wipe the page the user
        // was on in that tab; only deep links navigate.
        bool keepPlace = UrlRouter.IsAreaRoot(url);
        if (pinned.WebView?.CoreWebView2 is { } core)
        {
            if (!keepPlace && !string.Equals(core.Source, url, StringComparison.OrdinalIgnoreCase))
                core.Navigate(url);
        }
        else if (!keepPlace || UrlRouter.IsAreaRoot(pinned.Url))
        {
            pinned.Url = url; // lazy: loaded on first activation
        }
        if (activate) Activate(pinned);
        return pinned;
    }

    public void Activate(TabItemModel tab)
    {
        if (ReferenceEquals(Active, tab))
        {
            FocusActive();
            return;
        }

        var previous = Active;
        Active = tab;
        tab.IsActive = true;
        tab.IsSuspended = false;
        tab.LastActive = DateTime.UtcNow;

        // Show the new tab before hiding the old one so there is never an empty frame.
        if (tab.WebView is null)
        {
            _ = CreateWebViewAsync(tab);
        }
        else
        {
            tab.WebView.Visibility = Visibility.Visible; // auto-resumes a suspended renderer
            SetMemoryLevel(tab, CoreWebView2MemoryUsageTargetLevel.Normal);
        }

        if (previous is not null)
        {
            previous.IsActive = false;
            previous.LastActive = DateTime.UtcNow;
            if (previous.WebView is not null)
            {
                previous.WebView.Visibility = Visibility.Collapsed;
                SetMemoryLevel(previous, CoreWebView2MemoryUsageTargetLevel.Low);
            }
            if (previous.WebView?.CoreWebView2?.ContainsFullScreenElement == true)
                FullScreenRequested?.Invoke(false);
        }

        FocusActive();
        ActiveChanged?.Invoke();
    }

    public void Close(TabItemModel tab)
    {
        if (tab.IsPinned) return;
        int index = Tabs.IndexOf(tab);
        if (index < 0) return;

        _closed.Push(tab.Url);

        // Pinned tabs always exist, so there is always a neighbour to fall back to.
        if (ReferenceEquals(tab, Active))
            Activate(Tabs[index + 1 < Tabs.Count ? index + 1 : index - 1]);

        Tabs.RemoveAt(index);
        DisposeWebView(tab);
    }

    public void CloseActive()
    {
        if (Active is not null) Close(Active);
    }

    public void ReopenClosed()
    {
        if (_closed.TryPop(out var url))
            Open(url, activate: true, index: ActiveIndex + 1);
    }

    public void Move(int from, int to)
    {
        if (from == to || from < PinnedCount || to < PinnedCount || from >= Tabs.Count || to >= Tabs.Count) return;
        Tabs.Move(from, to);
    }

    public int ActiveIndex => Active is null ? -1 : Tabs.IndexOf(Active);

    public void SelectRelative(int delta)
    {
        if (Tabs.Count < 2) return;
        int i = ((ActiveIndex + delta) % Tabs.Count + Tabs.Count) % Tabs.Count;
        Activate(Tabs[i]);
    }

    public void SelectIndex(int index)
    {
        if (index >= 0 && index < Tabs.Count) Activate(Tabs[index]);
    }

    public void SelectLast()
    {
        if (Tabs.Count > 0) Activate(Tabs[^1]);
    }

    public void ReloadActive(bool ignoreCache)
    {
        var core = Active?.WebView?.CoreWebView2;
        if (core is null) return;
        if (ignoreCache)
            _ = core.CallDevToolsProtocolMethodAsync("Page.reload", "{\"ignoreCache\":true}");
        else
            core.Reload();
    }

    public void OpenDevTools() => Active?.WebView?.CoreWebView2?.OpenDevToolsWindow();

    public void FocusActive() => Active?.WebView?.Focus();

    // ---------------------------------------------------------------- WebView creation & wiring

    private async Task CreateWebViewAsync(TabItemModel tab)
    {
        var webView = new WebView2
        {
            DefaultBackgroundColor = PageBackground, // no white flash while loading
            Visibility = ReferenceEquals(tab, Active) ? Visibility.Visible : Visibility.Collapsed,
            Focusable = true,
        };
        tab.WebView = webView;
        tab.IsLoading = true;
        _host.Children.Add(webView);

        try
        {
            var env = await _environment;
            await webView.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowRuntimeMissing();
            tab.IsLoading = false;
            return;
        }
        catch (Exception) when (!ReferenceEquals(tab.WebView, webView))
        {
            return; // tab closed or discarded while initialising
        }
        catch (Exception ex)
        {
            tab.IsLoading = false;
            tab.Title = "Failed to load";
            System.Diagnostics.Debug.WriteLine($"WebView2 init failed: {ex}");
            return;
        }

        if (!ReferenceEquals(tab.WebView, webView)) return;

        var core = webView.CoreWebView2;
        WebViewFactory.Configure(core);
        Wire(tab, core);

        if (!ReferenceEquals(tab, Active))
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;

        core.Navigate(tab.Url);
        if (ReferenceEquals(tab, Active)) webView.Focus();
    }

    private void Wire(TabItemModel tab, CoreWebView2 core)
    {
        core.DocumentTitleChanged += (_, _) => tab.Title = CleanTitle(core.DocumentTitle);
        core.NavigationStarting += (_, e) =>
        {
            // Full-page navigations that belong in another tab are cancelled and re-homed.
            if (!e.IsRedirected && RouteToOtherTab(tab, e.Uri))
            {
                e.Cancel = true;
                // A file tab bounced to Home/Community without a click (missing file, no access…)
                // has nothing left to show — drop it. A user click ("Back to files") keeps the file open.
                if (!tab.IsPinned && !e.IsUserInitiated)
                    _host.Dispatcher.BeginInvoke(() => Close(tab));
                return;
            }
            tab.IsLoading = true;
        };
        core.SourceChanged += (_, _) => OnSourceChanged(tab, core);
        core.NavigationCompleted += (_, _) => tab.IsLoading = false;
        core.FaviconChanged += async (_, _) => tab.Favicon = await LoadFaviconAsync(core) ?? tab.Favicon;
        core.NewWindowRequested += (_, e) => OnNewWindowRequested(tab, e);
        core.WindowCloseRequested += (_, _) => Close(tab);
        core.PermissionRequested += OnPermissionRequested;
        core.ContainsFullScreenElementChanged += (_, _) =>
        {
            if (ReferenceEquals(tab, Active))
                FullScreenRequested?.Invoke(core.ContainsFullScreenElement);
        };
        core.ProcessFailed += (_, e) =>
        {
            // A crashed renderer only takes down this tab: reload it. (Unresponsive is NOT reloaded —
            // a heavy Figma file can legitimately block the main thread for a while.)
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
                core.Reload();
        };
    }

    /// <summary>
    /// Pinned tabs never show a file (files open in a new tab), file tabs never show Home/Community
    /// (those switch to the pinned tab). Returns true if the navigation was handled elsewhere.
    /// </summary>
    private bool RouteToOtherTab(TabItemModel tab, string url)
    {
        var area = UrlRouter.Classify(url);
        switch (area)
        {
            case FigmaArea.File when tab.IsPinned:
                Open(url);
                return true;
            case FigmaArea.Home when tab.Kind != TabKind.Home:
                ShowPinned(Home, url);
                return true;
            case FigmaArea.Community when tab.Kind != TabKind.Community:
                ShowPinned(Community, url);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Fallback for client-side (history.pushState) navigations, which don't raise NavigationStarting,
    /// and for server redirects (e.g. a deleted file redirecting to /files).
    /// </summary>
    private void OnSourceChanged(TabItemModel tab, CoreWebView2 core)
    {
        var previousUrl = tab.Url;
        var url = core.Source;
        tab.Url = url;

        var area = UrlRouter.Classify(url);
        bool misplaced = area switch
        {
            FigmaArea.File => tab.IsPinned,
            FigmaArea.Home => tab.Kind != TabKind.Home,
            FigmaArea.Community => tab.Kind != TabKind.Community,
            _ => false,
        };
        if (!misplaced)
        {
            if (tab.IsPinned)
            {
                // After bouncing a file out, GoBack can land on the root instead of the folder the user
                // was in. If so, send the tab to the remembered place.
                var target = tab.RestoreTarget;
                tab.RestoreTarget = null;
                if (target is not null && DateTime.UtcNow < tab.RestoreUntil &&
                    UrlRouter.IsAreaRoot(url) && !UrlRouter.IsAreaRoot(target) &&
                    !string.Equals(url, target, StringComparison.OrdinalIgnoreCase))
                {
                    _host.Dispatcher.BeginInvoke(() => { if (Tabs.Contains(tab)) core.Navigate(target); });
                    return;
                }
                tab.LastGoodUrl = url;
            }
            return;
        }

        // Defer: don't navigate/dispose a WebView from inside its own event.
        _host.Dispatcher.BeginInvoke(() =>
        {
            if (!Tabs.Contains(tab)) return;
            RouteToOtherTab(tab, url);

            if (tab.IsPinned)
            {
                // Put the pinned tab back where it was.
                var home = tab.Kind == TabKind.Home ? UrlRouter.HomeUrl : UrlRouter.CommunityUrl;
                tab.RestoreTarget = tab.LastGoodUrl;
                tab.RestoreUntil = DateTime.UtcNow.AddSeconds(5);
                if (core.CanGoBack) core.GoBack();
                else core.Navigate(tab.LastGoodUrl ?? home);
            }
            else
            {
                // The file tab left its file for Home/Community: that content now lives in the pinned tab.
                tab.Url = previousUrl; // so Ctrl+Shift+T reopens the file, not the home page
                Close(tab);
            }
        });
    }

    private void OnNewWindowRequested(TabItemModel source, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var features = e.WindowFeatures;
        bool popupFeatures = features.HasSize || features.HasPosition;

        switch (UrlRouter.Route(e.Uri, popupFeatures))
        {
            case RouteAction.InAppTab:
                e.Handled = true;
                bool background = Native.IsKeyDown(Native.VK_CONTROL) || Native.IsKeyDown(Native.VK_MBUTTON);
                int index = Tabs.IndexOf(source) + 1;
                Open(e.Uri, activate: !background, index: index > 0 ? index : null);
                break;

            case RouteAction.External:
                e.Handled = true;
                UrlRouter.OpenExternal(e.Uri);
                break;

            case RouteAction.Block:
                e.Handled = true;
                break;

            case RouteAction.Popup:
                // Leave unhandled: WebView2 opens its own popup window sharing this profile,
                // with window.opener intact (required by OAuth sign-in flows).
                break;
        }
    }

    private static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[perm] {e.PermissionKind} ({(int)e.PermissionKind}) from {e.Uri}");
        if (!UrlRouter.IsFigmaOrigin(e.Uri)) return;

        switch (e.PermissionKind)
        {
            case CoreWebView2PermissionKind.ClipboardRead:   // paste
            case CoreWebView2PermissionKind.LocalFonts:      // use installed fonts in designs
            case CoreWebView2PermissionKind.WindowManagement:// multi-screen presentation
            case CoreWebView2PermissionKind.PersistentStorage:
                e.State = CoreWebView2PermissionState.Allow;
                e.SavesInProfile = true;
                break;

            case CoreWebView2PermissionKind.Camera:
            case CoreWebView2PermissionKind.Microphone:
            case CoreWebView2PermissionKind.Geolocation:
                break; // leave to WebView2's default prompt

            default:
                // Kinds newer than this SDK (e.g. Chromium's local-network-access prompt, which gates the
                // Figma Agent font service on localhost:44950) arrive as unnamed enum values. Allow them
                // for Figma origins so the font agent works in every editor, not only Design.
                if (!Enum.IsDefined(e.PermissionKind))
                {
                    e.State = CoreWebView2PermissionState.Allow;
                    e.SavesInProfile = true;
                }
                break;
        }
    }

    // ---------------------------------------------------------------- memory management

    private static void SetMemoryLevel(TabItemModel tab, CoreWebView2MemoryUsageTargetLevel level)
    {
        if (tab.WebView?.CoreWebView2 is { } core)
        {
            try { core.MemoryUsageTargetLevel = level; }
            catch { /* older runtime */ }
        }
    }

    private async Task SuspendIdleTabsAsync()
    {
        var now = DateTime.UtcNow;
        foreach (var tab in Tabs.ToArray())
        {
            if (ReferenceEquals(tab, Active) || tab.WebView is null) continue;
            var idle = now - tab.LastActive;

            if (DiscardAfter is { } discard && idle > discard)
            {
                DisposeWebView(tab);
                tab.IsSuspended = true;
                continue;
            }

            if (!tab.IsSuspended && idle > SuspendAfter && tab.WebView.CoreWebView2 is { } core)
            {
                try
                {
                    bool ok = await core.TrySuspendAsync();
                    if (!ReferenceEquals(tab, Active)) tab.IsSuspended = ok;
                }
                catch
                {
                    // Not suspendable right now (e.g. audio playing) — try again next tick.
                }
            }
        }
    }

    private void DisposeWebView(TabItemModel tab)
    {
        if (tab.WebView is not { } webView) return;
        tab.WebView = null;
        _host.Children.Remove(webView);
        webView.Dispose();
    }

    // ---------------------------------------------------------------- helpers

    private static string CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Figma";
        foreach (var suffix in new[] { " – Figma", " - Figma", " — Figma", " | Figma" })
        {
            if (title.EndsWith(suffix, StringComparison.Ordinal) && title.Length > suffix.Length)
                return title[..^suffix.Length];
        }
        return title;
    }

    private static async Task<ImageSource?> LoadFaviconAsync(CoreWebView2 core)
    {
        try
        {
            using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (stream is null) return null;
            var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            if (buffer.Length == 0) return null;
            buffer.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 32;
            bmp.StreamSource = buffer;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private void ShowRuntimeMissing()
    {
        if (_runtimeErrorShown) return;
        _runtimeErrorShown = true;
        var result = MessageBox.Show(
            "FigmaShell needs the Microsoft Edge WebView2 Runtime, which was not found.\n\nOpen the download page now?",
            "WebView2 Runtime missing", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            UrlRouter.OpenExternal("https://developer.microsoft.com/microsoft-edge/webview2/");
    }
}
