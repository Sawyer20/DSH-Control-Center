// ============================================================================
//  Win11Backdrop.cs - Windows 11 system backdrop (Mica) + dark window chrome
//
//  Uses DWM attributes only (no WinForms, no third-party libs):
//    * DWMWA_USE_IMMERSIVE_DARK_MODE (20)   -> dark frame / system menus
//    * DWMWA_WINDOW_CORNER_PREFERENCE (33)  -> rounded window corners
//    * DWMWA_SYSTEMBACKDROP_TYPE (38)       -> Mica (Win11 22H2+, build 22621)
//
//  Mica is real, hardware-composited background blur: the window keeps
//  AllowsTransparency = false, so WPF text stays crisply rendered (unlike
//  layered/transparent windows, which fall back to software rendering).
//  When the OS cannot provide the backdrop (Windows 10, older 11 builds),
//  Apply() reports false and the caller keeps an opaque dark background.
// ============================================================================
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

internal static class Win11Backdrop
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_MAINWINDOW = 2;      // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

    /// <summary>True when the system backdrop was accepted by the compositor.</summary>
    public static bool Applied { get; private set; }

    /// <summary>Turn the Mica/acrylic backdrop on or off for this window.</summary>
    public static void SetBackdrop(Window window, bool enable)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int backdrop = enable ? DWMSBT_MAINWINDOW : DWMSBT_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4);
        }
        catch { }
    }

    /// <summary>Switch the window frame/chrome between dark and light system styling.</summary>
    public static void SetDarkMode(Window window, bool dark)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int value = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, 4);
        }
        catch { }
    }

    /// <summary>Apply dark chrome, rounded corners and (if possible) a Mica backdrop.</summary>
    public static void Apply(Window window, bool acrylic)
    {
        Applied = false;
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int on = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, 4);

            int corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);

            int backdrop = acrylic ? DWMSBT_TRANSIENTWINDOW : DWMSBT_MAINWINDOW;
            Applied = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4) == 0;
        }
        catch
        {
            Applied = false;
        }
    }
}
