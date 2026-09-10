// ============================================================================
//  TrayNative.cs - fully native tray icon + WPF self-drawn context menu
//
//  * Tray icon: Shell_NotifyIcon (Win32) with a hidden message window
//    (HwndSource) that routes left click / double click / right click.
//    NO System.Windows.Forms dependency anywhere.
//  * Menu: a borderless WPF Window (rounded card, theme-consistent) shown at
//    the cursor. Items are plain Borders + TextBlocks with hover states.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

internal static class TrayWin32
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    public static Point CursorScreen()
    {
        POINT p;
        if (GetCursorPos(out p)) return new Point(p.X, p.Y);
        return new Point(0, 0);
    }
}

// ============================================================================
//  NativeTray - Shell_NotifyIcon host (message-only window via HwndSource)
// ============================================================================
internal sealed class NativeTray : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public uint uTimeout;
    }

    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x0001;
    private const uint NIF_ICON = 0x0002;
    private const uint NIF_TIP = 0x0004;
    private const uint NIF_INFO = 0x0010;
    private const uint NIIF_INFO = 0x0001;
    private const uint NIIF_WARNING = 0x0002;
    private const uint NIIF_ERROR = 0x0003;

    // Mouse events delivered through uCallbackMessage (classic, pre-version mode)
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);

    private HwndSource _sink;
    private NOTIFYICONDATA _nid = new NOTIFYICONDATA();
    private readonly Dictionary<string, System.Drawing.Icon> _icons = new Dictionary<string, System.Drawing.Icon>();
    private string _iconPath = "";

    public event Action LeftClick;
    public event Action DoubleClick;
    public event Action RightClick;

    public NativeTray(string iconFile, string tip)
    {
        var prm = new HwndSourceParameters("dsh-tray-sink");
        prm.Width = 0;
        prm.Height = 0;
        prm.PositionX = 0;
        prm.PositionY = 0;
        prm.WindowStyle = unchecked((int)0x80000000);            // WS_POPUP (never shown)
        prm.ExtendedWindowStyle = unchecked((int)0x00000080);    // WS_EX_NOACTIVATE
        prm.HwndSourceHook = WndProc;
        _sink = new HwndSource(prm);

        _nid.cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA));
        _nid.hWnd = _sink.Handle;
        _nid.uID = 0x44485348;                                   // 'D','S','H'
        _nid.uCallbackMessage = 0x8000 + 1;                      // WM_APP + 1
        _nid.uFlags = NIF_MESSAGE | NIF_TIP;
        _nid.szTip = Limit(tip, 127);
        Shell_NotifyIcon(NIM_ADD, ref _nid);

        if (iconFile != null) UpdateIconFromFile(iconFile);
    }

    private static string Limit(string s, int max)
    {
        if (s == null) return "";
        return s.Length > max ? s.Substring(0, max) : s;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)_nid.uCallbackMessage)
        {
            handled = true;
            int code = lParam.ToInt32();
            if (code == WM_LBUTTONUP)
            {
                Action a = LeftClick;
                if (a != null) a();
            }
            else if (code == WM_LBUTTONDBLCLK)
            {
                Action a = DoubleClick;
                if (a != null) a();
            }
            else if (code == WM_RBUTTONUP)
            {
                Action a = RightClick;
                if (a != null) a();
            }
            return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    public void UpdateIconFromFile(string path)
    {
        if (path == null || path == _iconPath) return;
        // Ask the icon for the frame the shell actually draws (16 px at 100%
        // DPI, 24 px at 150%): new Icon(path) picks 32 px and lets Explorer
        // downscale it, which is what made the whale look soft in the tray.
        int size = TrayBadge.TraySize();
        string key = path + "|" + size;
        System.Drawing.Icon ico;
        if (!_icons.TryGetValue(key, out ico))
        {
            try { ico = new System.Drawing.Icon(path, size, size); }
            catch { return; }
            _icons[key] = ico;
        }
        _iconPath = path;
        _nid.hIcon = ico.Handle;
        _nid.uFlags = NIF_ICON;
        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    /// <summary>Swaps in an icon handle created by <see cref="TrayBadge"/> (red dot).</summary>
    public void UpdateIconHandle(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero || hIcon == _nid.hIcon) return;   // the 1s tick calls this
        _iconPath = "";                       // force the next file update through
        _nid.hIcon = hIcon;
        _nid.uFlags = NIF_ICON;
        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    public void UpdateTip(string tip)
    {
        string t = Limit(tip, 127);
        if (t == _nid.szTip) return;          // do not poke the shell every second
        _nid.szTip = t;
        _nid.uFlags = NIF_TIP;
        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    public void ShowBalloon(string title, string text, bool warning)
    {
        _nid.szInfo = Limit(text, 255);
        _nid.szInfoTitle = Limit(title, 63);
        _nid.dwInfoFlags = warning ? NIIF_WARNING : NIIF_INFO;
        _nid.uTimeout = 3000;
        _nid.uFlags = NIF_INFO;
        Shell_NotifyIcon(NIM_MODIFY, ref _nid);
    }

    public void Dispose()
    {
        try { Shell_NotifyIcon(NIM_DELETE, ref _nid); } catch { }
        if (_sink != null)
        {
            try { _sink.Dispose(); } catch { }
            _sink = null;
        }
        foreach (System.Drawing.Icon ico in _icons.Values)
        {
            try { ico.Dispose(); } catch { }
        }
        _icons.Clear();
    }
}

// ============================================================================
//  TrayBadge - composes the state icon with a red "unread" dot (cached HICONs)
//
//  Three traps made the tray icon look wrong:
//   1) our .ico files store every frame as a PNG (Vista-style PNG-in-ICO) and
//      System.Drawing.Icon cannot decode those - it returns noise. So we parse
//      the ICO directory ourselves and decode the PNG frame we want.
//   2) Icon.ToBitmap()/GetHicon() flatten the alpha channel. We draw onto a
//      32bpp ARGB bitmap and build the HICON with CreateIconIndirect from a
//      32bpp colour bitmap, which keeps per-pixel transparency.
//   3) a fixed 32px composition gets downscaled by the shell to the real tray
//      size (16px here) - the whale and its state dot turned to mush while the
//      badge stayed visually huge. We now compose at SM_CXSMICON and scale the
//      badge with it.
// ============================================================================
internal static class TrayBadge
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint cPlanes, uint cBitCount, IntPtr lpvBits);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPV5HEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct BITMAPV5HEADER
    {
        public int bV5Size; public int bV5Width; public int bV5Height;
        public short bV5Planes; public short bV5BitCount;
        public int bV5Compression; public int bV5SizeImage;
        public int bV5XPelsPerMeter; public int bV5YPelsPerMeter;
        public int bV5ClrUsed; public int bV5ClrImportant;
        public int bV5RedMask; public int bV5GreenMask; public int bV5BlueMask;
        public int bV5AlphaMask; public int bV5CSType;
    }

    private const int DIB_RGB_COLORS = 0;
    private const int BI_BITFIELDS = 3;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSMICON = 49;

    /// <summary>
    /// The size the notification area actually asks for (16 px at 100% DPI,
    /// 24 at 150%...). Composing at this size and decoding the matching native
    /// frame is what keeps the whale and its state dot crisp.
    /// </summary>
    internal static int TraySize()
    {
        int s = 16;
        try { s = GetSystemMetrics(SM_CXSMICON); } catch { }
        if (s < 16) s = 16;
        if (s > 64) s = 64;
        return s;
    }

    /// <summary>Badge diameter for a given icon size (kept in proportion).</summary>
    internal static int BadgeDiameter(int size)
    {
        int d = (int)Math.Round(size * 0.33);
        if (d < 4) d = 4;
        if (d > size / 2) d = size / 2;
        return d;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    private sealed class Entry
    {
        public IntPtr Handle;
        public System.Drawing.Bitmap Bitmap;
    }

    private static readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>();

    /// <summary>Returns a cached HICON for (icon file, badge on/off, tray size).</summary>
    internal static IntPtr Get(string iconFile, bool badge)
    {
        if (String.IsNullOrEmpty(iconFile)) return IntPtr.Zero;
        string key = iconFile + "|" + (badge ? "1" : "0") + "|" + TraySize();
        Entry e;
        if (_cache.TryGetValue(key, out e)) return e.Handle;
        try
        {
            var entry = new Entry();
            entry.Bitmap = Compose(iconFile, badge);
            if (entry.Bitmap == null) return IntPtr.Zero;
            entry.Handle = ToHIcon(entry.Bitmap);
            _cache[key] = entry;
            return entry.Handle;
        }
        catch { return IntPtr.Zero; }
    }

    /// <summary>
    /// Decodes the square frame closest to <paramref name="want"/> px. Handles
    /// both PNG-in-ICO frames (what our icon files use) and classic DIB frames.
    /// </summary>
    internal static System.Drawing.Bitmap LoadFrame(string icoPath, int want)
    {
        byte[] b = System.IO.File.ReadAllBytes(icoPath);
        if (b.Length < 22) return null;
        int count = BitConverter.ToUInt16(b, 4);
        int bestOff = -1, bestLen = 0, bestScore = int.MaxValue;
        for (int i = 0; i < count; i++)
        {
            int e = 6 + i * 16;
            if (e + 16 > b.Length) break;
            int w = b[e] == 0 ? 256 : b[e];
            int h = b[e + 1] == 0 ? 256 : b[e + 1];
            if (w != h) continue;
            int score = Math.Abs(w - want);
            if (score >= bestScore) continue;
            bestScore = score;
            bestOff = (int)BitConverter.ToUInt32(b, e + 12);
            bestLen = (int)BitConverter.ToUInt32(b, e + 8);
        }
        if (bestOff < 0 || bestLen <= 0 || bestOff + bestLen > b.Length) return null;

        var frame = new byte[bestLen];
        Array.Copy(b, bestOff, frame, 0, bestLen);

        if (frame[0] == 0x89 && frame[1] == 0x50)                  // PNG frame
        {
            using (var ms = new System.IO.MemoryStream(frame))
            using (var img = System.Drawing.Image.FromStream(ms))
                return new System.Drawing.Bitmap(img, new System.Drawing.Size(want, want));
        }
        using (var ms = new System.IO.MemoryStream(frame))          // DIB frame
        using (var ico = new System.Drawing.Icon(ms, want, want))
            return ico.ToBitmap();
    }

    /// <summary>
    /// Composes at <see cref="TraySize"/> (native tray resolution) and returns
    /// the ARGB bitmap; the smoke probe saves it as PNG.
    /// </summary>
    internal static System.Drawing.Bitmap Compose(string iconFile, bool badge)
    {
        return Compose(iconFile, badge, TraySize());
    }

    internal static System.Drawing.Bitmap Compose(string iconFile, bool badge, int size)
    {
        if (String.IsNullOrEmpty(iconFile) || !System.IO.File.Exists(iconFile)) return null;
        int S = size < 16 ? 16 : (size > 64 ? 64 : size);
        var bmp = new System.Drawing.Bitmap(S, S, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        System.Drawing.Bitmap frame = null;
        try
        {
            frame = LoadFrame(iconFile, S);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                if (frame != null) g.DrawImage(frame, new System.Drawing.Rectangle(0, 0, S, S));
                if (badge)
                {
                    // TOP-right on purpose: the state dot of tray_*.ico lives in
                    // the bottom-right corner, so a badge drawn there hid the
                    // service state entirely (green/amber/red/grey never showed).
                    // The dot scales with the icon so it stays a notification
                    // cue instead of dominating a 16px tray icon.
                    int d = BadgeDiameter(S);
                    int ring = S >= 32 ? 2 : 1;
                    int x = S - d - 1;
                    int y = 1;
                    using (var halo = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(230, 255, 255, 255)))
                        g.FillEllipse(halo, x - ring, y - ring, d + 2 * ring, d + 2 * ring);
                    using (var red = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 0xE5, 0x3E, 0x3E)))
                        g.FillEllipse(red, x, y, d, d);
                }
            }
        }
        finally { if (frame != null) frame.Dispose(); }
        return bmp;
    }

    private static IntPtr ToHIcon(System.Drawing.Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        byte[] argb = ArgbBytes(bmp);
        IntPtr hdc = IntPtr.Zero, hColor = IntPtr.Zero, hMask = IntPtr.Zero;
        try
        {
            hdc = GetDC(IntPtr.Zero);
            var v5 = new BITMAPV5HEADER();
            v5.bV5Size = Marshal.SizeOf(typeof(BITMAPV5HEADER));
            v5.bV5Width = w;
            v5.bV5Height = -h;                       // top-down, like the source
            v5.bV5Planes = 1;
            v5.bV5BitCount = 32;
            v5.bV5Compression = BI_BITFIELDS;
            v5.bV5RedMask = 0x00FF0000;
            v5.bV5GreenMask = 0x0000FF00;
            v5.bV5BlueMask = 0x000000FF;
            v5.bV5AlphaMask = unchecked((int)0xFF000000);
            IntPtr bits;
            hColor = CreateDIBSection(hdc, ref v5, DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
            if (hColor == IntPtr.Zero) return IntPtr.Zero;
            Marshal.Copy(argb, 0, bits, argb.Length);

            // AND mask: 1 = see-through. A zero-filled mask says "opaque
            // everywhere", which paints a black square around the whale in
            // every path that composites through the mask instead of the alpha.
            int stride = ((w + 31) / 32) * 4;
            byte[] mask = new byte[stride * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (argb[(y * w + x) * 4 + 3] < 128)
                        mask[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            GCHandle pin = GCHandle.Alloc(mask, GCHandleType.Pinned);
            try { hMask = CreateBitmap(w, h, 1, 1, pin.AddrOfPinnedObject()); }
            finally { pin.Free(); }
            if (hMask == IntPtr.Zero) return IntPtr.Zero;

            var ii = new ICONINFO();
            ii.fIcon = true;
            ii.hbmMask = hMask;
            ii.hbmColor = hColor;
            return CreateIconIndirect(ref ii);
        }
        finally
        {
            if (hMask != IntPtr.Zero) DeleteObject(hMask);
            if (hColor != IntPtr.Zero) DeleteObject(hColor);
            if (hdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    /// <summary>Packed top-down BGRA bytes of a 32bpp ARGB bitmap.</summary>
    private static byte[] ArgbBytes(System.Drawing.Bitmap bmp)
    {
        var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
        var d = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            int row = bmp.Width * 4;
            byte[] buf = new byte[row * bmp.Height];
            for (int y = 0; y < bmp.Height; y++)
                Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), buf, y * row, row);
            return buf;
        }
        finally { bmp.UnlockBits(d); }
    }

    internal static void Free()
    {
        foreach (Entry e in _cache.Values)
        {
            try { if (e.Handle != IntPtr.Zero) DestroyIcon(e.Handle); } catch { }
            try { if (e.Bitmap != null) e.Bitmap.Dispose(); } catch { }
        }
        _cache.Clear();
    }
}

// ============================================================================
//  TrayMenuItem - one entry of the tray popup
// ============================================================================
internal sealed class TrayMenuItem
{
    public string Text;
    public bool Separator;
    public Action Act;

    public TrayMenuItem(string text) { Text = text; }
    public static TrayMenuItem Sep() { return new TrayMenuItem("") { Separator = true }; }
}

// ============================================================================
//  TrayMenuPopup - rounded WPF card menu shown at the cursor
// ============================================================================
internal sealed class TrayMenuPopup : Window
{
    private readonly StackPanel _rows = new StackPanel();
    private const double CardWidth = 236;
    private const double ItemH = 36;
    private const double SepH = 12;

    public TrayMenuPopup()
    {
        Title = "";
        Width = CardWidth;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Height;
        FontFamily = WpfTheme.Ui;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        // same policy as the main window (layered popup => WPF uses grayscale)
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);

        Deactivated += delegate { SafeClose(); };
        KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) SafeClose();
        };

        var root = new Grid();
        root.Margin = new Thickness(11);

        // Soft shadow drawn by a dedicated element so the content stays crisp.
        var shadow = new Border
        {
            CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(Color.FromArgb(8, 0, 0, 0)),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 17,
                ShadowDepth = 1,
                Opacity = 0.28,
                Color = Color.FromRgb(16, 24, 40)
            }
        };

        var card = new Border
        {
            CornerRadius = new CornerRadius(13),
            Background = WpfTheme.CardSolid,
            BorderBrush = WpfTheme.Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Child = _rows
        };
        root.Children.Add(shadow);
        root.Children.Add(card);
        Content = root;
    }

    private void SafeClose()
    {
        try { Close(); } catch { }
    }

    public void ShowAt(Point cursor)
    {
        double h = 22;                                  // margins + padding
        for (int i = 0; i < _rows.Children.Count; i++)
        {
            UserControl r = _rows.Children[i] as UserControl;
            if (r != null) h += ItemH + 2;
            else h += SepH;
        }
        h = Math.Max(h, 46);

        Rect wa = SystemParameters.WorkArea;
        double x = cursor.X;
        double y = cursor.Y - 2;
        double w = CardWidth + 22;
        if (x + w > wa.Right) x = wa.Right - w - 4;
        if (x < wa.Left) x = wa.Left + 4;
        if (y + h > wa.Bottom) y = wa.Bottom - h - 4;
        if (y < wa.Top) y = wa.Top + 4;
        Left = x;
        Top = y;
        Show();
        Activate();
        TrayWin32.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    public void BuildAndShow(Point cursor, IList<TrayMenuItem> items)
    {
        _rows.Children.Clear();
        foreach (TrayMenuItem it in items)
        {
            if (it.Separator)
            {
                var sep = new Border
                {
                    Height = SepH - 6,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(14, 3, 14, 3),
                    Background = WpfTheme.Border,
                    BorderThickness = new Thickness(0)
                };
                var holder = new Border { Height = SepH, Child = sep };
                _rows.Children.Add(holder);
                continue;
            }
            _rows.Children.Add(BuildRow(it));
        }
        ShowAt(cursor);
    }

    private UserControl BuildRow(TrayMenuItem it)
    {
        var row = new UserControl { Height = ItemH, Margin = new Thickness(0, 1, 0, 1) };
        var b = new Border { CornerRadius = new CornerRadius(8), Background = Brushes.Transparent };
        var lb = new TextBlock
        {
            Text = it.Text,
            FontSize = 13,
            Foreground = WpfTheme.TextPrimary,
            Margin = new Thickness(14, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        b.Child = lb;
        row.Content = b;

        row.MouseEnter += delegate
        {
            b.Background = WpfTheme.AccentSoft;
            lb.Foreground = WpfTheme.Accent;
        };
        row.MouseLeave += delegate
        {
            b.Background = Brushes.Transparent;
            lb.Foreground = WpfTheme.TextPrimary;
        };
        row.MouseLeftButtonUp += delegate
        {
            Action act = it.Act;
            SafeClose();
            if (act != null) act();
        };
        return row;
    }
}
