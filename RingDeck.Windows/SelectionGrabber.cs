using System.Runtime.InteropServices;
using RingDeck.Core;

namespace RingDeck.Windows;

/// <summary>Reads the text selected in the focused app by sending Ctrl+C, then puts the user's clipboard back.
/// Call on an STA thread (the clipboard needs one).</summary>
public static class SelectionGrabber
{
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();

    public static async Task<string?> GrabAsync(InputSender input, ClipboardService clipboard, CancellationToken ct)
    {
        var before = GetClipboardSequenceNumber();
        var saved = clipboard.Capture();
        input.ReleaseModifiers();
        var changed = false;
        try
        {
            input.SendCombo(new Shortcut(Mods.Ctrl, 0x43));
            for (var waited = 0; waited < 300 && !changed; waited += 20)
            {
                await Task.Delay(20, ct);
                changed = GetClipboardSequenceNumber() != before;
            }
            if (!changed) return null;                         // nothing selected, or the app ignores Ctrl+C
            await Task.Delay(30, ct);                          // let the app finish writing every format
            return clipboard.GetText();
        }
        finally
        {
            if (changed) clipboard.Restore(saved);
            input.RestoreModifiers();
        }
    }
}

public static class Focus
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);

    public static IntPtr Foreground() => NativeMethods.GetForegroundWindow();

    /// <summary>Gives focus back to a window. Works while one of RingDeck's own windows is in front.</summary>
    public static bool Restore(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9 /* SW_RESTORE */);
        return SetForegroundWindow(hwnd);
    }
}
