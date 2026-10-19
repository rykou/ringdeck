using System.ComponentModel;
using System.Runtime.InteropServices;
using RingDeck.Core;
using static RingDeck.Windows.NativeMethods;

namespace RingDeck.Windows;

public sealed class InputSender : IInputSender
{
    const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;
    const int MaskVk = 0xE8;   // unassigned key: stops a lone Alt or Win release from opening a menu
    static readonly int[] ModifierVks = { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C };

    readonly Func<int, bool>? _physicallyDown;
    List<int> _released = new();

    /// <param name="physicallyDown">Tells whether the user is really holding a modifier key (from the input hook).</param>
    public InputSender(Func<int, bool>? physicallyDown = null) => _physicallyDown = physicallyDown;

    static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>Modifier keys held right now, read from the OS.</summary>
    public static Mods CurrentMods() =>
        (IsDown(0x11) ? Mods.Ctrl : 0) | (IsDown(0x12) ? Mods.Alt : 0) | (IsDown(0x10) ? Mods.Shift : 0)
        | (IsDown(0x5B) || IsDown(0x5C) ? Mods.Win : 0);

    public bool CanSendToForeground()
    {
        if (Environment.IsPrivilegedProcess) return true;
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return true;
        GetWindowThreadProcessId(window, out var pid);
        var process = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
        if (process == IntPtr.Zero) return Marshal.GetLastWin32Error() != 5;   // access denied: a higher-privilege process
        try
        {
            if (!OpenProcessToken(process, 0x0008 /* TOKEN_QUERY */, out var token)) return Marshal.GetLastWin32Error() != 5;
            try { return !(GetTokenInformation(token, 20 /* TokenElevation */, out var elevated, 4, out _) && elevated != 0); }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    public void ReleaseModifiers()
    {
        var held = ModifierVks.Where(IsDown).ToList();
        _released = held;
        if (held.Count == 0) return;
        var inputs = new List<INPUT> { Key(MaskVk, false), Key(MaskVk, true) };
        inputs.AddRange(held.Select(vk => Key(vk, true)));
        Send(inputs);
    }

    /// <summary>Gives back the modifiers ReleaseModifiers let go, so a user who keeps Ctrl+Alt held can press the
    /// ring's key again straight away.</summary>
    public void RestoreModifiers()
    {
        var again = _released.Where(vk => _physicallyDown?.Invoke(vk) == true).ToList();
        _released = new();
        if (again.Count == 0) return;
        var inputs = again.Select(vk => Key(vk, false)).ToList();
        inputs.Add(Key(MaskVk, false));
        inputs.Add(Key(MaskVk, true));
        Send(inputs);
    }

    public void SendMask() => Send(new List<INPUT> { Key(MaskVk, false), Key(MaskVk, true) });

    public void SendCombo(Shortcut combo)
    {
        if (combo.Vk < 0x08) throw new InvalidOperationException("Mouse buttons cannot be used in a macro.");
        var mods = new List<int>();
        if (combo.Mods.HasFlag(Mods.Ctrl)) mods.Add(0xA2);
        if (combo.Mods.HasFlag(Mods.Alt)) mods.Add(0xA4);
        if (combo.Mods.HasFlag(Mods.Shift)) mods.Add(0xA0);
        if (combo.Mods.HasFlag(Mods.Win)) mods.Add(0x5B);
        var inputs = mods.Select(vk => Key(vk, false)).ToList();
        inputs.Add(Key(combo.Vk, false));
        inputs.Add(Key(combo.Vk, true));
        inputs.AddRange(Enumerable.Reverse(mods).Select(vk => Key(vk, true)));
        Send(inputs);
    }

    public void SendText(string text)
    {
        var inputs = new List<INPUT>();
        foreach (var c in text)
        {
            switch (c)
            {
                case '\r': continue;
                case '\n': inputs.Add(Key(0x0D, false)); inputs.Add(Key(0x0D, true)); break;
                case '\t': inputs.Add(Key(0x09, false)); inputs.Add(Key(0x09, true)); break;
                default: inputs.Add(Unicode(c, false)); inputs.Add(Unicode(c, true)); break;
            }
            if (inputs.Count >= 128) { Send(inputs); inputs.Clear(); }
        }
        Send(inputs);
    }

    public void SendKey(int vk, bool up)
    {
        if (vk < 0x08) throw new InvalidOperationException("Mouse buttons cannot be used as keys.");
        Send(new List<INPUT> { Key(vk, up) });
    }

    public void SendMouseButton(ClickButton button, bool up)
    {
        uint flags = button switch
        {
            ClickButton.Right => up ? 0x0010u : 0x0008u,
            ClickButton.Middle => up ? 0x0040u : 0x0020u,
            _ => up ? 0x0004u : 0x0002u,
        };
        var input = new INPUT { Type = 0 };
        input.U.Mouse = new MOUSEINPUT { Flags = flags };
        Send(new List<INPUT> { input });
    }

    public void MoveMouse(int x, int y) => Screens.MoveCursor(x, y);

    public void Scroll(ScrollDirection direction, int notches)
    {
        const uint Wheel = 0x0800, HWheel = 0x1000;
        var horizontal = direction is ScrollDirection.Left or ScrollDirection.Right;
        var delta = direction is ScrollDirection.Up or ScrollDirection.Right ? 120 : -120;
        var inputs = new List<INPUT>();
        for (var i = 0; i < notches; i++)
        {
            var input = new INPUT { Type = 0 };
            input.U.Mouse = new MOUSEINPUT { MouseData = unchecked((uint)delta), Flags = horizontal ? HWheel : Wheel };
            inputs.Add(input);
        }
        Send(inputs);
    }

    static bool IsExtended(int vk) => vk is (>= 0x21 and <= 0x28) or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0xA3 or 0xA5 or 0x6F;

    static INPUT Key(int vk, bool up)
    {
        var input = new INPUT { Type = 1 };
        input.U.Keyboard = new KEYBDINPUT
        {
            Vk = (ushort)vk, Scan = (ushort)MapVirtualKey((uint)vk, 0),
            Flags = (up ? KEYEVENTF_KEYUP : 0u) | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0u),
        };
        return input;
    }

    static INPUT Unicode(char c, bool up)
    {
        var input = new INPUT { Type = 1 };
        input.U.Keyboard = new KEYBDINPUT { Scan = c, Flags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0u) };
        return input;
    }

    static void Send(List<INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != inputs.Count)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows blocked the input RingDeck tried to send.");
    }
}
