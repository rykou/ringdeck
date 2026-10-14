namespace RingDeck.Core.Tests;

public class HotkeyMatcherTests
{
    static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();
    const int Q = 0x51, W = 0x57, LCtrl = 0xA2;

    static HotkeyMatcher Make()
    {
        var m = new HotkeyMatcher();
        m.SetBindings(new[] { (new Shortcut(Mods.Ctrl | Mods.Alt, Q), A), (new Shortcut(Mods.None, 0x05), B) });
        return m;
    }

    [Fact]
    public void PressAndReleaseOfBoundCombo()
    {
        var m = Make();
        Assert.Equal(new HotkeyResult(true, A, null), m.OnKey(Q, true, Mods.Ctrl | Mods.Alt));
        Assert.Equal(new HotkeyResult(true, null, null), m.OnKey(Q, true, Mods.Ctrl | Mods.Alt));   // auto-repeat
        Assert.Equal(new HotkeyResult(true, null, A), m.OnKey(Q, false, Mods.Ctrl | Mods.Alt));
    }

    [Fact]
    public void ModifiersMustMatchExactly()
    {
        var m = Make();
        Assert.Equal(default, m.OnKey(Q, true, Mods.Ctrl));
        Assert.Equal(default, m.OnKey(Q, false, Mods.Ctrl));
        Assert.Equal(default, m.OnKey(Q, true, Mods.Ctrl | Mods.Alt | Mods.Shift));
    }

    [Fact]
    public void ReleasingModifierFirstStillReleasesOnMainKeyUp()
    {
        var m = Make();
        m.OnKey(Q, true, Mods.Ctrl | Mods.Alt);
        Assert.Equal(default, m.OnKey(LCtrl, false, Mods.Alt));
        Assert.Equal(new HotkeyResult(true, null, A), m.OnKey(Q, false, Mods.None));
    }

    [Fact]
    public void OtherKeysPassThroughWhileHeld()
    {
        var m = Make();
        m.OnKey(Q, true, Mods.Ctrl | Mods.Alt);
        Assert.Equal(default, m.OnKey(W, true, Mods.Ctrl | Mods.Alt));
        Assert.Equal(default, m.OnKey(0x05, true, Mods.None));   // a second binding does not start while one is held
    }

    [Fact]
    public void MouseButtonBindingWorks()
    {
        var m = Make();
        Assert.Equal(new HotkeyResult(true, B, null), m.OnKey(0x05, true, Mods.None));
        Assert.Equal(new HotkeyResult(true, null, B), m.OnKey(0x05, false, Mods.None));
    }

    [Fact]
    public void PausedMatcherIgnoresPressesButFinishesAHeldOne()
    {
        var m = Make();
        m.OnKey(Q, true, Mods.Ctrl | Mods.Alt);
        m.Paused = true;
        Assert.Equal(new HotkeyResult(true, null, A), m.OnKey(Q, false, Mods.None));
        Assert.Equal(default, m.OnKey(Q, true, Mods.Ctrl | Mods.Alt));
    }

    [Fact]
    public void RebindingWhileHeldDropsTheHeldKey()
    {
        var m = Make();
        m.OnKey(Q, true, Mods.Ctrl | Mods.Alt);
        m.SetBindings(Array.Empty<(Shortcut, Guid)>());
        Assert.Equal(default, m.OnKey(Q, false, Mods.None));
    }

    [Fact]
    public void DuplicateBindingsKeepTheFirst()
    {
        var m = new HotkeyMatcher();
        m.SetBindings(new[] { (new Shortcut(Mods.None, Q), A), (new Shortcut(Mods.None, Q), B) });
        Assert.Equal(A, m.OnKey(Q, true, Mods.None).Pressed);
    }

    [Fact]
    public void ChooserPicksAmongRingsSharingAShortcut()
    {
        var m = new HotkeyMatcher();
        m.SetBindings(new[] { (new Shortcut(Mods.None, Q), A), (new Shortcut(Mods.None, Q), B) });
        IReadOnlyList<Guid>? offered = null;
        var r = m.OnKey(Q, true, Mods.None, ids => { offered = ids; return B; });
        Assert.Equal(new[] { A, B }, offered);
        Assert.Equal(new HotkeyResult(true, B, null), r);
        Assert.Equal(new HotkeyResult(true, null, B), m.OnKey(Q, false, Mods.None));
    }

    [Fact]
    public void WhenTheChooserFindsNothingTheKeyPassesThrough()
    {
        var m = Make();
        Assert.Equal(default, m.OnKey(Q, true, Mods.Ctrl | Mods.Alt, _ => null));
        Assert.Equal(default, m.OnKey(Q, false, Mods.Ctrl | Mods.Alt));    // its key-up is not swallowed either
    }
}
