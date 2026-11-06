using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Microsoft.Web.WebView2.Wpf;

namespace FigmaShell.Tabs;

public enum TabKind
{
    /// <summary>A regular, closable tab holding a Figma file.</summary>
    File,
    /// <summary>Permanent icon-only tab: file browser (figma.com/files).</summary>
    Home,
    /// <summary>Permanent icon-only tab: figma.com/community.</summary>
    Community,
}

public sealed class TabItemModel(string url, TabKind kind = TabKind.File) : INotifyPropertyChanged
{
    private string _title = "Figma";
    private string _url = url;
    private ImageSource? _favicon;
    private bool _isLoading;
    private bool _isActive;
    private bool _isSuspended;

    public TabKind Kind { get; } = kind;
    public bool IsPinned => Kind != TabKind.File;

    /// <summary>Segoe Fluent Icons glyph shown for pinned tabs.</summary>
    public string Glyph => Kind switch
    {
        TabKind.Home => "",      // Home
        TabKind.Community => "", // Globe
        _ => "",
    };

    public string PinnedLabel => Kind switch
    {
        TabKind.Home => "Home",
        TabKind.Community => "Community",
        _ => "",
    };

    public string Title { get => _title; set => Set(ref _title, value); }
    public string Url { get => _url; set => Set(ref _url, value); }
    public ImageSource? Favicon { get => _favicon; set => Set(ref _favicon, value); }
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    /// <summary>Renderer is suspended (TrySuspendAsync) or discarded / not yet loaded.</summary>
    public bool IsSuspended { get => _isSuspended; set => Set(ref _isSuspended, value); }

    /// <summary>Null until the tab is first activated (lazy) or after it has been discarded.</summary>
    public WebView2? WebView { get; set; }

    public DateTime LastActive { get; set; } = DateTime.UtcNow;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
