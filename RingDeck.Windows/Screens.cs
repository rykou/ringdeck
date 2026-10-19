using static RingDeck.Windows.NativeMethods;

namespace RingDeck.Windows;

/// <summary>Work area of a monitor in physical pixels, and its scale (1.0 = 96 dpi).</summary>
public readonly record struct ScreenInfo(int Left, int Top, int Right, int Bottom, double DpiScale);

public static class Screens
{
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOACTIVATE = 0x10, SWP_NOSIZE = 0x01, SWP_SHOWWINDOW = 0x40;
    static readonly IntPtr Topmost = new(-1);

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);

    public static void MoveCursor(int x, int y) => SetCursorPos(x, y);

    public static (int X, int Y) Cursor() => GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    /// <summary>The top-level window actually showing at a screen point (physical pixels): the one on top there.</summary>
    public static IntPtr TopWindowAt(int x, int y)
    {
        var hwnd = WindowFromPoint(new POINT { X = x, Y = y });
        return hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, 2 /* GA_ROOT */);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);

    /// <summary>The whole area of the monitor at a point (taskbar included), in physical pixels.</summary>
    public static (int X, int Y, int Width, int Height) MonitorBounds(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, 2 /* nearest */);
        var info = new MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return (0, 0, 1920, 1080);
        return (info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
    }

    /// <summary>All monitors together, in physical pixels.</summary>
    public static (int X, int Y, int Width, int Height) VirtualBounds() =>
        (GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

    /// <summary>The window in front, without its invisible resize border, in physical pixels; null when there is none.</summary>
    public static (int X, int Y, int Width, int Height)? ForegroundBounds()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        var size = System.Runtime.InteropServices.Marshal.SizeOf<RECT>();
        if (DwmGetWindowAttribute(hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out var r, size) != 0 && !GetWindowRect(hwnd, out r)) return null;
        return r.Right > r.Left && r.Bottom > r.Top ? (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : null;
    }

    public static ScreenInfo At(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, 2 /* nearest */);
        var info = new MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info)) return new ScreenInfo(0, 0, 1920, 1080, 1);
        var dpi = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX : 96;
        return new ScreenInfo(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom, dpi / 96.0);
    }

    /// <summary>Makes a window click-through, never focused, and hidden from Alt+Tab.</summary>
    public static void MakeOverlay(IntPtr hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
    }

    /// <summary>Positions a square window in physical pixels. Called twice on purpose: when the first call moves the
    /// window to a monitor with a different DPI, WPF resizes it, and the second call restores the size.</summary>
    public static void Place(IntPtr hwnd, int x, int y, int size)
    {
        SetWindowPos(hwnd, Topmost, x, y, size, size, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        SetWindowPos(hwnd, Topmost, x, y, size, size, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>Positions a window in physical pixels, on top. Called twice, as in <see cref="Place"/>.</summary>
    public static void PlaceRect(IntPtr hwnd, int x, int y, int width, int height)
    {
        SetWindowPos(hwnd, Topmost, x, y, width, height, SWP_SHOWWINDOW);
        SetWindowPos(hwnd, Topmost, x, y, width, height, SWP_SHOWWINDOW);
    }

    public static void Park(IntPtr hwnd) => SetWindowPos(hwnd, Topmost, -32000, -32000, 0, 0, SWP_NOACTIVATE | SWP_NOSIZE);
}
