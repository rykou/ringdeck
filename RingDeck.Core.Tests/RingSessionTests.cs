namespace RingDeck.Core.Tests;

public class RingSessionTests
{
    readonly Ring _child = new() { Name = "child", Slices = { new Slice { Label = "c0", Action = new PasteTextAction { Text = "c" } }, new Slice { Label = "c1", Action = new PasteTextAction { Text = "d" } } } };
    readonly Ring _empty = new() { Name = "empty" };
    readonly Ring _root;
    readonly RingSession _s;

    public RingSessionTests()
    {
        _root = new Ring
        {
            Name = "root", Radius = 100,
            Slices =
            {
                new Slice { Label = "up", Action = new PasteTextAction { Text = "a" } },
                new Slice { Label = "right", Action = new NestedRingAction { RingId = _child.Id } },
                new Slice { Label = "down", Action = new NestedRingAction { RingId = Guid.NewGuid() } },
                new Slice { Label = "left", Action = new NestedRingAction { RingId = _empty.Id } },
            },
        };
        var all = new[] { _root, _child, _empty };
        _s = new RingSession(id => all.FirstOrDefault(r => r.Id == id));
    }

    [Fact]
    public void HoldModeReleaseRunsSliceAndCloses()
    {
        _s.Open(_root, 500, 500);
        Assert.True(_s.IsOpen);
        Assert.Equal(_root.Id, _s.RootId);
        Assert.False(_s.PointerMoved(500, 400));
        Assert.Equal(0, _s.Selected);
        var r = _s.Release(500, 400);
        Assert.Equal(ChooseKind.Run, r.Kind);
        Assert.Equal("up", r.Slice!.Label);
        Assert.False(_s.IsOpen);
    }

    [Fact]
    public void ReleaseInDeadZoneClosesWithoutAction()
    {
        _s.Open(_root, 500, 500);
        Assert.Equal(new ChooseResult(ChooseKind.Closed, null), _s.Release(505, 505));
        Assert.False(_s.IsOpen);
    }

    [Fact]
    public void ClickModeIgnoresReleaseAndWaitsForClick()
    {
        _root.Mode = OpenMode.Click;
        _s.Open(_root, 500, 500);
        Assert.True(_s.Sticky);
        Assert.Equal(ChooseKind.None, _s.Release(500, 400).Kind);
        Assert.True(_s.IsOpen);
        Assert.Equal(ChooseKind.Run, _s.Choose(500, 400).Kind);
    }

    [Fact]
    public void PushThroughEntersChildCentredOnPointer()
    {
        _s.Open(_root, 500, 500);
        Assert.False(_s.PointerMoved(600, 500));          // on the slice, not past the threshold
        Assert.True(_s.PointerMoved(500 + 171, 500));     // beyond 1.7 x radius
        Assert.Same(_child, _s.Current);
        Assert.Equal(1, _s.Depth);
        Assert.Equal((671.0, 500.0), (_s.CenterX, _s.CenterY));
        Assert.Equal(-1, _s.Selected);
        Assert.False(_s.Sticky);
        Assert.Equal(_root.Id, _s.RootId);
        Assert.Equal("c0", _s.Release(671, 400).Slice!.Label);
    }

    [Fact]
    public void PushThroughDoesNothingOnOrdinarySlice()
    {
        _s.Open(_root, 500, 500);
        Assert.False(_s.PointerMoved(500, 100));
        Assert.Same(_root, _s.Current);
    }

    [Fact]
    public void ReleaseOnNestedSliceEntersAndBecomesSticky()
    {
        _s.Open(_root, 500, 500);
        var r = _s.Release(600, 500);
        Assert.Equal(ChooseKind.EnteredNested, r.Kind);
        Assert.Same(_child, _s.Current);
        Assert.True(_s.Sticky);
        Assert.Equal(ChooseKind.None, _s.Release(600, 500).Kind);
        Assert.False(_s.PointerMoved(600 + 500, 500));    // no push-through once sticky
        Assert.Equal(ChooseKind.Run, _s.Choose(600, 600).Kind);
    }

    [Fact]
    public void MissingOrEmptyNestedRingClosesAndReportsIt()
    {
        _s.Open(_root, 500, 500);
        var missing = _s.Release(500, 600);
        Assert.Equal(ChooseKind.MissingRing, missing.Kind);
        Assert.Equal("down", missing.Slice!.Label);
        Assert.False(_s.IsOpen);

        _s.Open(_root, 500, 500);
        Assert.Equal(ChooseKind.MissingRing, _s.Release(400, 500).Kind);

        _s.Open(_root, 500, 500);
        Assert.False(_s.PointerMoved(500, 900));          // push-through onto a missing ring: stay put
        Assert.Same(_root, _s.Current);
    }

    [Fact]
    public void BackReturnsToParentThenClosesRoot()
    {
        _s.Open(_root, 500, 500);
        _s.Release(600, 500);
        Assert.True(_s.Back());
        Assert.Same(_root, _s.Current);
        Assert.Equal((500.0, 500.0), (_s.CenterX, _s.CenterY));
        Assert.False(_s.Back());
        Assert.False(_s.IsOpen);
    }

    [Fact]
    public void SliceWithoutActionJustCloses()
    {
        _root.Slices[0].Action = null;
        _s.Open(_root, 500, 500);
        Assert.Equal(ChooseKind.Closed, _s.Release(500, 400).Kind);
    }

    [Fact]
    public void SelfNestingStopsAtMaxDepth()
    {
        var loop = new Ring { Radius = 100, Mode = OpenMode.Click };
        loop.Slices.Add(new Slice { Action = new NestedRingAction { RingId = loop.Id } });
        var s = new RingSession(id => id == loop.Id ? loop : null);
        s.Open(loop, 0, 0);
        var last = ChooseKind.EnteredNested;
        for (var i = 0; i < RingSession.MaxDepth + 5 && s.IsOpen; i++) last = s.Choose(s.CenterX, s.CenterY - 100).Kind;
        Assert.Equal(ChooseKind.MissingRing, last);
        Assert.False(s.IsOpen);
    }

    [Fact]
    public void CallsOnClosedSessionAreHarmless()
    {
        Assert.False(_s.PointerMoved(1, 1));
        Assert.Equal(ChooseKind.None, _s.Choose(1, 1).Kind);
        Assert.Equal(ChooseKind.None, _s.Release(1, 1).Kind);
        Assert.False(_s.Back());
    }
}
