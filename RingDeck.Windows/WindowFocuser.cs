using System.Runtime.InteropServices;
using System.Text;
using RingDeck.Core;

namespace RingDeck.Windows;

/// <summary>Finds top-level windows by program and title, and brings one to the front (the "focus window" step).</summary>
public sealed class WindowFocuser(InputSender input) : IWindowFocuser
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    public readonly record struct OpenWindow(IntPtr Handle, string Program, string Title);

    /// <summary>Visible, titled top-level windows, front-most first.</summary>
    public static List<OpenWindow> OpenWindows()
    {
        var found = new List<OpenWindow>();
        var names = new Dictionary<uint, string>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
            var length = GetWindowTextLength(hwnd);
            if (length == 0) return true;
            var text = new StringBuilder(length + 1);
            GetWindowText(hwnd, text, text.Capacity);
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId) return true;
            if (!names.TryGetValue(pid, out var program))
            {
                try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); program = p.ProcessName + ".exe"; }
                catch (Exception) { program = ""; }
                names[pid] = program;
            }
            found.Add(new OpenWindow(hwnd, program, text.ToString()));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public bool Focus(string program, string title)
    {
        var window = OpenWindows().FirstOrDefault(w =>
            (program.Length == 0 || ProgramNames.AnyMatch(new[] { program }, w.Program))
            && (title.Length == 0 || w.Title.Contains(title, StringComparison.CurrentCultureIgnoreCase)));
        if (window.Handle == IntPtr.Zero) return false;
        if (IsIconic(window.Handle)) ShowWindow(window.Handle, 9 /* SW_RESTORE */);
        // Windows only lets the program that last received input change the foreground window. A harmless
        // key press makes that true for us; attaching to the current foreground thread is the fallback.
        input.SendMask();
        if (SetForegroundWindow(window.Handle)) return true;
        var foreground = GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        var mine = GetCurrentThreadId();
        AttachThreadInput(mine, foreground, true);
        try { BringWindowToTop(window.Handle); return SetForegroundWindow(window.Handle); }
        finally { AttachThreadInput(mine, foreground, false); }
    }
}
