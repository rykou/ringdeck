namespace RingDeck.Core.Tests;

public class MacroRecorderTests
{
    [Fact]
    public void MergesTypingAndKeepsCombosSeparate()
    {
        var r = new MacroRecorder();
        r.OnKeyDown(0x48, Mods.Shift);          // H
        r.OnKeyDown(0x49, Mods.None);           // i
        r.OnKeyDown(0x20, Mods.None);           // space
        r.OnKeyDown(0x32, Mods.None);           // 2
        r.OnKeyDown(0xA2, Mods.Ctrl);           // left Ctrl on its own: ignored
        r.OnKeyDown(0x53, Mods.Ctrl);           // Ctrl+S
        r.OnKeyDown(0x0D, Mods.None);           // Enter
        r.OnKeyDown(0x31, Mods.Shift);          // Shift+1 is a symbol whose character depends on layout
        r.OnKeyDown(0x58, Mods.None);           // x
        r.OnKeyDown(0x01, Mods.None);           // left mouse button: ignored
        Assert.Collection(r.Steps,
            s => Assert.Equal("Hi 2", Assert.IsType<TextStep>(s).Text),
            s => Assert.Equal("Ctrl+S", Assert.IsType<KeyStep>(s).Combo),
            s => Assert.Equal("Enter", Assert.IsType<KeyStep>(s).Combo),
            s => Assert.Equal("Shift+1", Assert.IsType<KeyStep>(s).Combo),
            s => Assert.Equal("x", Assert.IsType<TextStep>(s).Text));
    }
}
