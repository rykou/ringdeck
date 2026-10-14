using System.Runtime.InteropServices;
using System.Text;
using RingDeck.Core;

namespace RingDeck.Windows;

/// <summary>Reports which program has focus and whether its window covers the whole screen.</summary>
public static class ForegroundProbe
{
    const int GWL_STYLE = -16;
    const long WS_CAPTION = 0x00C00000, WS_MAXIMIZE = 0x01000000;

    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeMethods.RECT rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    public static ForegroundApp Get()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero || window == GetShellWindow()) return default;
        NativeMethods.GetWindowThreadProcessId(window, out var pid);
        if (pid == Environment.ProcessId) return default;                 // RingDeck's own editor never counts

        var klass = new StringBuilder(64);
        GetClassName(window, klass, klass.Capacity);
        if (klass.ToString() is "Progman" or "WorkerW") return default;   // the desktop itself covers the screen

        return new ForegroundApp(ExeName(pid), CoversMonitor(window));
    }

    static bool CoversMonitor(IntPtr window)
    {
        var info = new NativeMethods.MONITORINFO { Size = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!GetWindowRect(window, out var r) || !NativeMethods.GetMonitorInfo(MonitorFromWindow(window, 2), ref info)) return false;
        var m = info.Monitor;
        if (r.Left > m.Left || r.Top > m.Top || r.Right < m.Right || r.Bottom < m.Bottom) return false;
        // An ordinary maximized window (title bar and all) can also cover the screen when the taskbar auto-hides.
        var style = NativeMethods.GetWindowLongPtr(window, GWL_STYLE).ToInt64();
        return !((style & WS_MAXIMIZE) != 0 && (style & WS_CAPTION) == WS_CAPTION);
    }

    static string? ExeName(uint pid)
    {
        var process = NativeMethods.OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            var name = new StringBuilder(1024);
            var size = name.Capacity;
            return QueryFullProcessImageName(process, 0, name, ref size) ? Path.GetFileName(name.ToString()) : null;
        }
        finally { NativeMethods.CloseHandle(process); }
    }
}
