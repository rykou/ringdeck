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

    public static (int X, int Y) Cursor() => GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);

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

    public static void Park(IntPtr hwnd) => SetWindowPos(hwnd, Topmost, -32000, -32000, 0, 0, SWP_NOACTIVATE | SWP_NOSIZE);
}
