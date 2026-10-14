namespace RingDeck.Core.Tests;

public class SliceMoverTests
{
    readonly Ring _a = new() { Name = "A" }, _b = new() { Name = "B" };
    readonly Slice _s = new() { Label = "Signature", Action = new PasteTextAction { Text = "Best" } };
    readonly AppConfig _config;

    public SliceMoverTests()
    {
        _a.Slices.Add(_s);
        _a.Slices.Add(new Slice { Label = "Other" });
        _b.Slices.Add(new Slice { Label = "B1" });
        _config = new AppConfig { Rings = { _a, _b } };
    }

    [Fact]
    public void MoveTakesTheSliceOutAndAddsItAtTheEnd()
    {
        Assert.Null(SliceMover.Transfer(_config, _a, _s, _b, copy: false));
        Assert.DoesNotContain(_s, _a.Slices);
        Assert.Same(_s, _b.Slices[^1]);
        Assert.Equal(2, _b.Slices.Count);
    }

    [Fact]
    public void CopyLeavesTheOriginalAndKeepsTheLabel()
    {
        Assert.Null(SliceMover.Transfer(_config, _a, _s, _b, copy: true));
        Assert.Contains(_s, _a.Slices);
        var copy = _b.Slices[^1];
        Assert.NotSame(_s, copy);
        Assert.NotEqual(_s.Id, copy.Id);
        Assert.Equal("Signature", copy.Label);
        ((PasteTextAction)copy.Action!).Text = "changed";
        Assert.Equal("Best", ((PasteTextAction)_s.Action!).Text);
    }

    [Fact]
    public void MovingOntoItsOwnRingChangesNothing()
    {
        Assert.Null(SliceMover.Transfer(_config, _a, _s, _a, copy: false));
        Assert.Equal(new[] { "Signature", "Other" }, _a.Slices.Select(s => s.Label));
    }

    [Fact]
    public void CopyingOntoItsOwnRingAddsACopy()
    {
        Assert.Null(SliceMover.Transfer(_config, _a, _s, _a, copy: true));
        Assert.Equal(new[] { "Signature", "Other", "Signature" }, _a.Slices.Select(s => s.Label));
    }

    [Fact]
    public void SliceThatWouldMakeARingOpenItselfIsRefused()
    {
        var opener = new Slice { Label = "Open B", Action = new NestedRingAction { RingId = _b.Id } };
        _a.Slices.Add(opener);
        var error = SliceMover.Transfer(_config, _a, opener, _b, copy: false);
        Assert.NotNull(error);
        Assert.Contains("Open B", error);
        Assert.Contains(opener, _a.Slices);                  // nothing changed
        Assert.DoesNotContain(opener, _b.Slices);
    }

    [Fact]
    public void IndirectLoopIsRefusedToo()
    {
        var c = new Ring { Name = "C" };
        c.Slices.Add(new Slice { Action = new NestedRingAction { RingId = _b.Id } });   // C opens B
        _config.Rings.Add(c);
        var opener = new Slice { Label = "Open C", Action = new NestedRingAction { RingId = c.Id } };
        _a.Slices.Add(opener);
        Assert.NotNull(SliceMover.Transfer(_config, _a, opener, _b, copy: true));   // B -> C -> B
        Assert.Null(SliceMover.Transfer(_config, _a, opener, _a, copy: true));      // A -> C -> B is fine
    }

    [Fact]
    public void SliceNotInTheSourceRingIsRefused() =>
        Assert.NotNull(SliceMover.Transfer(_config, _b, _s, _a, copy: false));
}
