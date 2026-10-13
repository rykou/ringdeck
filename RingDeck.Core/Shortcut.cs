namespace RingDeck.Core;

[Flags]
public enum Mods { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

/// <summary>A key (Windows virtual-key code) with modifiers. Mouse buttons use their VK codes 4, 5 and 6.</summary>
public readonly record struct Shortcut(Mods Mods, int Vk)
{
    public bool IsEmpty => Vk == 0;

    public static bool TryParse(string? text, out Shortcut shortcut)
    {
        shortcut = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = Mods.None;
        var vk = 0;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Mods.Ctrl; continue;
                case "alt": mods |= Mods.Alt; continue;
                case "shift": mods |= Mods.Shift; continue;
                case "win": mods |= Mods.Win; continue;
            }
            if (vk != 0 || !KeyNames.TryGetVk(part, out vk)) return false;
        }
        if (vk == 0) return false;
        shortcut = new Shortcut(mods, vk);
        return true;
    }

    public override string ToString()
    {
        if (IsEmpty) return "";
        var parts = new List<string>();
        if (Mods.HasFlag(Mods.Ctrl)) parts.Add("Ctrl");
        if (Mods.HasFlag(Mods.Alt)) parts.Add("Alt");
        if (Mods.HasFlag(Mods.Shift)) parts.Add("Shift");
        if (Mods.HasFlag(Mods.Win)) parts.Add("Win");
        parts.Add(KeyNames.NameOf(Vk));
        return string.Join("+", parts);
    }
}

public static class KeyNames
{
    static readonly Dictionary<string, int> ByName = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<int, string> ByVk = new();

    static KeyNames()
    {
        for (var c = 'A'; c <= 'Z'; c++) Add(c.ToString(), c);
        for (var d = 0; d <= 9; d++) Add(d.ToString(), 0x30 + d);
        for (var f = 1; f <= 24; f++) Add("F" + f, 0x6F + f);
        for (var n = 0; n <= 9; n++) Add("Numpad" + n, 0x60 + n);
        (string, int)[] named =
        {
            ("MouseMiddle", 0x04), ("MouseX1", 0x05), ("MouseX2", 0x06),
            ("Backspace", 0x08), ("Tab", 0x09), ("Enter", 0x0D), ("Pause", 0x13), ("CapsLock", 0x14), ("Esc", 0x1B),
            ("Space", 0x20), ("PageUp", 0x21), ("PageDown", 0x22), ("End", 0x23), ("Home", 0x24),
            ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28), ("PrintScreen", 0x2C),
            ("Insert", 0x2D), ("Delete", 0x2E), ("Menu", 0x5D), ("NumLock", 0x90), ("ScrollLock", 0x91),
            ("AbntSlash", 0xC1), ("AbntNumpadDot", 0xC2), ("IsoBackslash", 0xE2),
            ("NumpadMultiply", 0x6A), ("NumpadAdd", 0x6B), ("NumpadSubtract", 0x6D), ("NumpadDecimal", 0x6E),
            ("NumpadDivide", 0x6F),
            ("Semicolon", 0xBA), ("Plus", 0xBB), ("Comma", 0xBC), ("Minus", 0xBD), ("Period", 0xBE), ("Slash", 0xBF),
            ("Backquote", 0xC0), ("LeftBracket", 0xDB), ("Backslash", 0xDC), ("RightBracket", 0xDD), ("Quote", 0xDE),
        };
        foreach (var (name, vk) in named) Add(name, vk);
        ByName["Escape"] = 0x1B;
        ByName["Return"] = 0x0D;
        ByName["Del"] = 0x2E;
    }

    static void Add(string name, int vk) { ByName[name] = vk; ByVk[vk] = name; }

    public static bool TryGetVk(string name, out int vk)
    {
        if (ByName.TryGetValue(name, out vk)) return true;
        // Keys with no friendly name are written as Key0xNN, the form NameOf produces.
        return name.StartsWith("Key0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(5), System.Globalization.NumberStyles.HexNumber, null, out vk)
            && vk is > 0 and < 0xFF;
    }

    public static string NameOf(int vk) => ByVk.TryGetValue(vk, out var name) ? name : $"Key0x{vk:X2}";

    /// <summary>Shift, Ctrl, Alt and Win keys, generic and left/right.</summary>
    public static bool IsModifier(int vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);
}
