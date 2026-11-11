using System.Diagnostics;

namespace FigmaShell.Web;

public enum RouteAction
{
    /// <summary>Open as a new tab inside FigmaShell.</summary>
    InAppTab,
    /// <summary>Let WebView2 open its default popup window (OAuth / sign-in flows that need window.opener).</summary>
    Popup,
    /// <summary>Hand off to the user's default browser.</summary>
    External,
    /// <summary>Drop silently (unknown / unsafe scheme).</summary>
    Block,
}

/// <summary>Which part of figma.com a URL belongs to — decides which tab it should live in.</summary>
public enum FigmaArea
{
    Other,
    Home,
    Community,
    File,
}

public static class UrlRouter
{
    public const string HomeUrl = "https://www.figma.com/files";
    public const string CommunityUrl = "https://www.figma.com/community";
    /// <summary>Creates a new design draft (this is where figma.new redirects to).</summary>
    public const string NewFileUrl = "https://www.figma.com/file/new";
    public const string NewBoardUrl = "https://www.figma.com/board/new";
    public const string NewSlidesUrl = "https://www.figma.com/slides/new";

    public const string NewMakeUrl = "https://www.figma.com/make/new";
    public const string NewSpecUrl = "https://www.figma.com/spec/new";
    public const string NewBuzzUrl = "https://www.figma.com/buzz/new";
    public const string NewSiteUrl = "https://www.figma.com/site/new";

    private static readonly HashSet<string> FileRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "file", "design", "board", "proto", "slides", "deck", "make", "site", "buzz", "spec",
    };

    /// <summary>File key of an editor URL (/design/KEY/name), or null for non-file and "/new" URLs.
    /// /file/KEY redirects to /design/KEY, so the key is what identifies "the same file".</summary>
    public static string? GetFileKey(string? url)
    {
        if (Classify(url) != FigmaArea.File) return null;
        var segs = new Uri(url!).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length < 2) return null;
        // /proto/KEY and /make/KEY etc. all carry the key second; "new" is not a file.
        return segs[1].Equals("new", StringComparison.OrdinalIgnoreCase) ? null : segs[1];
    }

    /// <summary>True for the bare /files or /community landing pages (no sub-path).</summary>
    public static bool IsAreaRoot(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !IsFigmaHost(u)) return false;
        return u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length <= 1;
    }

    public static FigmaArea Classify(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !IsFigmaHost(u))
            return FigmaArea.Other;

        var first = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (first.Equals("files", StringComparison.OrdinalIgnoreCase)) return FigmaArea.Home;
        if (first.Equals("community", StringComparison.OrdinalIgnoreCase)) return FigmaArea.Community;
        if (FileRoots.Contains(first)) return FigmaArea.File;
        return FigmaArea.Other;
    }

    private static readonly HashSet<string> AuthHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "accounts.google.com",
        "appleid.apple.com",
        "login.microsoftonline.com",
        "login.live.com",
    };

    private static readonly string[] FigmaAuthPaths = ["/login", "/oauth", "/saml", "/sso", "/signup", "/logout"];

    public static bool IsFigmaHost(Uri u) =>
        u.Host.Equals("figma.com", StringComparison.OrdinalIgnoreCase) ||
        u.Host.Equals("www.figma.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>figma.com or any subdomain — for permission grants only (editors may run in subdomain frames).
    /// Deliberately separate from <see cref="IsFigmaHost"/>, which decides in-app vs external link routing.</summary>
    public static bool IsFigmaOrigin(string? s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        (u.Host.Equals("figma.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.EndsWith(".figma.com", StringComparison.OrdinalIgnoreCase));

    public static bool IsFigmaUrl(string? s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) &&
        IsFigmaHost(u);

    /// <param name="uri">Target of window.open / target=_blank.</param>
    /// <param name="hasPopupFeatures">True when the page asked for a sized/positioned window (typical for OAuth popups).</param>
    public static RouteAction Route(string uri, bool hasPopupFeatures)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u))
            return RouteAction.Popup;

        if (u.Scheme == "about")
            return RouteAction.Popup; // about:blank popups get navigated by script afterwards

        if (u.Scheme == "mailto")
            return RouteAction.External;

        if (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp)
            return RouteAction.Block;

        if (IsFigmaHost(u))
        {
            var path = u.AbsolutePath;
            if (hasPopupFeatures || FigmaAuthPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                return RouteAction.Popup;
            return RouteAction.InAppTab;
        }

        if (hasPopupFeatures || AuthHosts.Contains(u.Host))
            return RouteAction.Popup;

        return RouteAction.External;
    }

    public static void OpenExternal(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch
        {
            // No handler registered / user cancelled — nothing sensible to do.
        }
    }
}
