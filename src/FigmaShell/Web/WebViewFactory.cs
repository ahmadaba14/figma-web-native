using System.IO;
using Microsoft.Web.WebView2.Core;

namespace FigmaShell.Web;

public static class WebViewFactory
{
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FigmaShell");

    // Figma is a WebGL/WASM canvas app: make sure GPU raster is on regardless of blocklist heuristics
    // for rasterising DOM UI. (Everything else stays at Chromium defaults, which are already tuned.)
    // The second flag lets figma.com reach the Figma Agent font service on localhost:44950. Chromium's
    // Local Network Access check needs a permission prompt that a WebView2 host never shows, so the
    // request is blocked silently and non-Design editors report "disconnected from the font installer".
    private const string BrowserArguments =
        "--enable-gpu-rasterization " +
        "--disable-features=LocalNetworkAccessChecks,PrivateNetworkAccessSendPreflights," +
        "PrivateNetworkAccessRespectPreflightResults,BlockInsecurePrivateNetworkRequests";

    /// <summary>
    /// One environment for the whole app: every tab shares one browser process, one GPU process and
    /// one profile (so the Figma login is shared), with a separate renderer per tab.
    /// </summary>
    public static Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        var options = new CoreWebView2EnvironmentOptions(BrowserArguments);
        return CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: Path.Combine(DataRoot, "WebView2"),
            options: options);
    }

    public static void Configure(CoreWebView2 core)
    {
        var s = core.Settings;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;      // Ctrl+wheel belongs to the Figma canvas
        s.IsPinchZoomEnabled = false;        // pinch belongs to the Figma canvas
        s.IsSwipeNavigationEnabled = false;  // no accidental back-navigation on trackpads
        s.AreBrowserAcceleratorKeysEnabled = false; // Ctrl+R/F/P/G etc. go to Figma, not the browser
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = true;
#if DEBUG
        s.AreDevToolsEnabled = true;
#else
        s.AreDevToolsEnabled = false;
#endif
    }
}
