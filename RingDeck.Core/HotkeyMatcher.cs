namespace RingDeck.Core;

/// <summary>What the hook should do with a key event: swallow it, and which ring's shortcut went down or up.</summary>
public readonly record struct HotkeyResult(bool Swallow, Guid? Pressed, Guid? Released);

/// <summary>Turns raw key events into ring shortcut presses and releases. Modifier state is passed in by the caller
/// (read from the OS) so it cannot drift when events are missed.</summary>
public sealed class HotkeyMatcher
{
    readonly Dictionary<Shortcut, Guid> _bindings = new();
    int _heldVk;
    Guid _heldId;

    public bool Paused { get; set; }

    public void SetBindings(IEnumerable<(Shortcut Shortcut, Guid Id)> bindings)
    {
        _bindings.Clear();
        foreach (var (shortcut, id) in bindings)
            if (!shortcut.IsEmpty) _bindings.TryAdd(shortcut, id);
        _heldVk = 0;
    }

    public void Reset() => _heldVk = 0;

    public HotkeyResult OnKey(int vk, bool down, Mods mods)
    {
        if (KeyNames.IsModifier(vk)) return default;
        if (_heldVk != 0 && vk == _heldVk)
        {
            if (down) return new HotkeyResult(true, null, null);
            _heldVk = 0;
            return new HotkeyResult(true, null, _heldId);
        }
        if (!down || Paused || _heldVk != 0) return default;
        if (!_bindings.TryGetValue(new Shortcut(mods, vk), out var id)) return default;
        _heldVk = vk;
        _heldId = id;
        return new HotkeyResult(true, id, null);
    }
}
