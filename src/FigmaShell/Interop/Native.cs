using System.Runtime.InteropServices;

namespace FigmaShell.Interop;

internal static class Native
{
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_NCLBUTTONUP = 0x00A2;
    public const int WM_NCLBUTTONDBLCLK = 0x00A3;
    public const int WM_NCMOUSELEAVE = 0x02A2;
    public const int HTMAXBUTTON = 9;

    public const int VK_MBUTTON = 0x04;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;

    public const int SM_CXSIZEFRAME = 32;
    public const int SM_CYSIZEFRAME = 33;
    public const int SM_CXPADDEDBORDER = 92;

    public const int ASFW_ANY = -1;

    // DWM window attributes
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWCP_ROUND = 2;

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Dark frame, rounded corners and a border/caption colour matching the tab strip.</summary>
    public static void ApplyDarkFrame(IntPtr hwnd, int colorRef)
    {
        int on = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
    }
}
