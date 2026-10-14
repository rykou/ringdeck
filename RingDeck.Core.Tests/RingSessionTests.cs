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
        _s = new RingSession(id => all.FirstOrDefault(r => r.Id == id)) { HoverOpens = false };   // timing-free; see NestedHoverTests
    }

    [Fact]
    public void HoldModeReleaseRunsSliceAndCloses()
    {
        _s.Open(_root, 500, 500);
        Assert.True(_s.IsOpen);
        Assert.Equal(_root.Id, _s.RootId);
        Assert.Equal(PointerChange.None, _s.PointerMoved(500, 400));
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

    /// <summary>Where slice <paramref name="i"/> of the current ring is on screen.</summary>
    (double X, double Y) SlicePoint(int i)
    {
        var (x, y) = RingGeometry.PointAt(_s.Layout.Angle(i), _s.Radius);
        return (_s.CenterX + x, _s.CenterY + y);
    }

    [Fact]
    public void PushThroughOpensTheChildFurtherOutAndKeepsTheParent()
    {
        _s.Open(_root, 500, 500);
        Assert.Equal(PointerChange.None, _s.PointerMoved(600, 500));          // on the slice, not past the threshold
        Assert.Equal(PointerChange.Entered, _s.PointerMoved(500 + 171, 500));     // pushed past it
        Assert.Same(_child, _s.Current);
        Assert.Equal(1, _s.Depth);
        // the child sits to the right of the "right" slice, not under the pointer, and the root stays in the trail
        Assert.True(_s.CenterX > 600 + RingSession.SliceSize);
        Assert.Equal(500, _s.CenterY, 6);
        var parent = Assert.Single(_s.Trail);
        Assert.Same(_root, parent.Ring);
        Assert.Equal(1, parent.Selected);
        Assert.Equal((600.0, 500.0), (Math.Round(_s.Link!.Value.X, 6), Math.Round(_s.Link.Value.Y, 6)));
        Assert.Equal(-1, _s.Selected);
        Assert.False(_s.Sticky);
        Assert.Equal(_root.Id, _s.RootId);
        var (x, y) = SlicePoint(0);
        Assert.Equal("c0", _s.Release(x, y).Slice!.Label);
    }

    [Fact]
    public void ANestedRingFansAwayFromItsParent()
    {
        _s.Open(_root, 500, 500);
        _s.PointerMoved(671, 500);
        Assert.False(_s.Layout.IsFull);
        // no slice of the child points back towards the root (to the left, 270 degrees)
        for (var i = 0; i < _child.Slices.Count; i++)
        {
            var off = Math.Abs(((_s.Layout.Angle(i) - 270) % 360 + 540) % 360 - 180);
            Assert.True(off >= RingLayout.BackGap / 2, $"slice {i} at {_s.Layout.Angle(i)}");
        }
        Assert.True(_s.PointsBack(_s.CenterX - 100, _s.CenterY));
        Assert.False(_s.PointsBack(_s.CenterX + 100, _s.CenterY));
    }

    [Fact]
    public void LettingGoOverTheGapClosesEverything()
    {
        _s.Open(_root, 500, 500);
        _s.PointerMoved(671, 500);
        Assert.Equal(ChooseKind.Closed, _s.Release(_s.CenterX - 100, _s.CenterY).Kind);
        Assert.False(_s.IsOpen);
    }

    [Fact]
    public void ClickingInTheGapReturnsToTheParent()
    {
        _s.Open(_root, 500, 500);
        _s.PointerMoved(671, 500);
        var r = _s.Choose(_s.CenterX - 100, _s.CenterY);
        Assert.Equal(ChooseKind.WentBack, r.Kind);
        Assert.Same(_root, _s.Current);
        Assert.Empty(_s.Trail);
        Assert.True(_s.Sticky);
        Assert.True(_s.IsOpen);
    }

    [Fact]
    public void AClampedChildStillFacesItsParent()
    {
        _s.Open(_root, 500, 500);
        _s.PointerMoved(671, 500);
        _s.Recenter(_s.CenterX - 40, _s.CenterY + 200);   // pushed up from the bottom of the screen, say
        var link = _s.Link!.Value;
        Assert.True(_s.PointsBack(link.X, link.Y));
    }

    [Fact]
    public void LayoutsFindSlicesByDirection()
    {
        var full = RingLayout.Full(4);
        Assert.Equal(new[] { 0, 1, 2, 3 }, new[] { full.SliceAt(0, -50, 10), full.SliceAt(50, 0, 10), full.SliceAt(0, 50, 10), full.SliceAt(-50, 0, 10) });
        Assert.Equal(-1, full.SliceAt(2, 2, 10));
        var fan = RingLayout.Fan(2, 270);                 // parent to the left: slices to the upper and lower right
        Assert.Equal(-1, fan.SliceAt(-50, 0, 10));
        Assert.Equal(0, fan.SliceAt(30, -50, 10));
        Assert.Equal(1, fan.SliceAt(30, 50, 10));
        Assert.True(RingLayout.Fan(12, 0).RadiusFor(60, 56) > RingLayout.Full(12).RadiusFor(60, 56));
    }

    [Fact]
    public void BackAfterPushThroughStaysOnParentUntilPointerComesBackIn()
    {
        _s.Open(_root, 500, 500);
        Assert.Equal(PointerChange.Entered, _s.PointerMoved(671, 500));
        Assert.True(_s.Back());
        Assert.Equal(PointerChange.None, _s.PointerMoved(671, 500));          // still far out: must not bounce back into the child
        Assert.Same(_root, _s.Current);
        Assert.Equal(1, _s.Selected);
        Assert.Equal(PointerChange.None, _s.PointerMoved(560, 500));          // back inside
        Assert.Equal(PointerChange.Entered, _s.PointerMoved(671, 500));           // a fresh push enters again
        Assert.Same(_child, _s.Current);
    }

    [Fact]
    public void PushThroughIsReachableBeforeTheClampedScreenEdge()
    {
        _s.Open(_root, 500, 500);
        Assert.Equal(PointerChange.Entered, _s.PointerMoved(500 + _s.Radius + RingSession.SliceSize + 1, 500));
    }

    [Fact]
    public void PushThroughDoesNothingOnOrdinarySlice()
    {
        _s.Open(_root, 500, 500);
        Assert.Equal(PointerChange.None, _s.PointerMoved(500, 100));
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
        Assert.Equal(PointerChange.None, _s.PointerMoved(600 + 500, 500));    // no push-through once sticky
        var (x, y) = SlicePoint(1);
        Assert.Equal(ChooseKind.Run, _s.Choose(x, y).Kind);
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
        Assert.Equal(PointerChange.None, _s.PointerMoved(500, 900));          // push-through onto a missing ring: stay put
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
        Assert.Equal(PointerChange.None, _s.PointerMoved(1, 1));
        Assert.Equal(ChooseKind.None, _s.Choose(1, 1).Kind);
        Assert.Equal(ChooseKind.None, _s.Release(1, 1).Kind);
        Assert.False(_s.Back());
    }
}
