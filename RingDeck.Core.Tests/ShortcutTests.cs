namespace RingDeck.Core.Tests;

public class ShortcutTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Q", Mods.Ctrl | Mods.Alt, 0x51)]
    [InlineData("ctrl + shift + f5", Mods.Ctrl | Mods.Shift, 0x74)]
    [InlineData("Win+Space", Mods.Win, 0x20)]
    [InlineData("Enter", Mods.None, 0x0D)]
    [InlineData("Alt+MouseX1", Mods.Alt, 0x05)]
    [InlineData("Ctrl+Plus", Mods.Ctrl, 0xBB)]
    public void ParsesCombos(string text, Mods mods, int vk)
    {
        Assert.True(Shortcut.TryParse(text, out var s));
        Assert.Equal(new Shortcut(mods, vk), s);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Q+W")]
    [InlineData("Ctrl+Nope")]
    public void RejectsBadCombos(string? text) => Assert.False(Shortcut.TryParse(text, out _));

    [Fact]
    public void RoundTripsThroughText()
    {
        var s = new Shortcut(Mods.Ctrl | Mods.Shift | Mods.Win, 0x70);
        Assert.Equal("Ctrl+Shift+Win+F1", s.ToString());
        Assert.True(Shortcut.TryParse(s.ToString(), out var back));
        Assert.Equal(s, back);
    }

    [Fact]
    public void EveryNamedKeyRoundTrips()
    {
        foreach (var vk in Enumerable.Range(1, 254))
        {
            var name = KeyNames.NameOf(vk);
            if (name.StartsWith("Key0x")) continue;
            Assert.True(KeyNames.TryGetVk(name, out var back), name);
            Assert.Equal(vk, back);
        }
    }
}
