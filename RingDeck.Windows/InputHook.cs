using System.ComponentModel;
using System.Runtime.InteropServices;
using static RingDeck.Windows.NativeMethods;

namespace RingDeck.Windows;

/// <summary>Low-level keyboard and mouse-button hooks. Install on the UI thread; the callback runs there and must
/// return quickly. Events injected by any program (including RingDeck's own SendInput) are passed through unreported.</summary>
public sealed class InputHook : IDisposable
{
    const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    const uint LLKHF_INJECTED = 0x10, LLMHF_INJECTED = 0x01;

    readonly HookProc _keyboardProc, _mouseProc;   // fields keep the delegates alive for the native side
    IntPtr _keyboard, _mouse;

    public InputHook() { _keyboardProc = OnKeyboard; _mouseProc = OnMouse; }

    /// <summary>(virtual key, is down) => true to swallow the event.</summary>
    public Func<int, bool, bool>? Key { get; set; }
    /// <summary>(wheel delta, horizontal) => true to swallow the event. 120 per notch; positive is up, or right.</summary>
    public Func<int, bool, bool>? Wheel { get; set; }
    /// <summary>Receives exceptions thrown by the Key callback (they must not escape into the hook chain).</summary>
    public Action<Exception>? Error { get; set; }

    readonly HashSet<int> _heldModifiers = new();

    /// <summary>True while the user is physically holding this modifier key, judged from real (not injected) events.</summary>
    public bool IsPhysicallyDown(int vk) { lock (_heldModifiers) return _heldModifiers.Contains(vk); }

    public void Install()
    {
        var module = GetModuleHandle(null);
        _keyboard = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        _mouse = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        if (_keyboard == IntPtr.Zero || _mouse == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "RingDeck could not install its input hooks.");
        }
    }

    IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var message = (int)wParam;
            if ((info.Flags & LLKHF_INJECTED) == 0)
            {
                var down = message is WM_KEYDOWN or WM_SYSKEYDOWN;
                if (RingDeck.Core.KeyNames.IsModifier((int)info.Vk))
                    lock (_heldModifiers) { if (down) _heldModifiers.Add((int)info.Vk); else _heldModifiers.Remove((int)info.Vk); }
                if (Report((int)info.Vk, down)) return 1;
            }
        }
        return CallNextHookEx(_keyboard, code, wParam, lParam);
    }

    IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if ((int)wParam is 0x20A or 0x20E)          // WM_MOUSEWHEEL, WM_MOUSEHWHEEL
            {
                if (Wheel is { } wheel && (info.Flags & LLMHF_INJECTED) == 0)
                {
                    try { if (wheel((short)(info.MouseData >> 16), (int)wParam == 0x20E)) return 1; }
                    catch (Exception ex) { Error?.Invoke(ex); }
                }
                return CallNextHookEx(_mouse, code, wParam, lParam);
            }
            var x = (info.MouseData >> 16) == 1 ? 0x05 : 0x06;
            (int vk, bool down) = (int)wParam switch
            {
                0x201 => (0x01, true), 0x202 => (0x01, false),
                0x204 => (0x02, true), 0x205 => (0x02, false),
                0x207 => (0x04, true), 0x208 => (0x04, false),
                0x20B => (x, true), 0x20C => (x, false),
                _ => (0, false),
            };
            if (vk != 0 && (info.Flags & LLMHF_INJECTED) == 0 && Report(vk, down)) return 1;
        }
        return CallNextHookEx(_mouse, code, wParam, lParam);
    }

    bool Report(int vk, bool down)
    {
        try { return Key?.Invoke(vk, down) ?? false; }
        catch (Exception ex) { Error?.Invoke(ex); return false; }   // an exception must never escape into the hook chain
    }

    public void Dispose()
    {
        if (_keyboard != IntPtr.Zero) UnhookWindowsHookEx(_keyboard);
        if (_mouse != IntPtr.Zero) UnhookWindowsHookEx(_mouse);
        _keyboard = _mouse = IntPtr.Zero;
    }
}
