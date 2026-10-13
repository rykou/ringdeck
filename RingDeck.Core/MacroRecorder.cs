namespace RingDeck.Core;

/// <summary>Builds macro steps from key presses. Plain letters, digits and space merge into typed text; anything
/// else becomes a key-combo step. Timing is not recorded.</summary>
public sealed class MacroRecorder
{
    public List<MacroStep> Steps { get; } = new();

    public void OnKeyDown(int vk, Mods mods)
    {
        if (vk < 0x08 || KeyNames.IsModifier(vk)) return;
        if ((mods & ~Mods.Shift) == 0 && Printable(vk, mods.HasFlag(Mods.Shift)) is { } c)
        {
            if (Steps.LastOrDefault() is TextStep last) last.Text += c;
            else Steps.Add(new TextStep { Text = c.ToString() });
            return;
        }
        Steps.Add(new KeyStep { Combo = new Shortcut(mods, vk).ToString() });
    }

    static char? Printable(int vk, bool shift) => vk switch
    {
        >= 0x41 and <= 0x5A => (char)(shift ? vk : vk + 32),
        >= 0x30 and <= 0x39 when !shift => (char)vk,
        0x20 when !shift => ' ',
        _ => null,
    };
}
