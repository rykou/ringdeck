namespace RingDeck.Core.Tests;

public class NestedHoverTests
{
    long _now;
    readonly Ring _child = new() { Name = "child", Slices = { new Slice { Label = "c0", Action = new PasteTextAction() }, new Slice { Label = "c1", Action = new PasteTextAction() } } };
    readonly Ring _root;
    readonly RingSession _s;

    public NestedHoverTests()
    {
        _root = new Ring
        {
            Name = "root", Radius = 100,
            Slices =
            {
                new Slice { Label = "up", Action = new PasteTextAction() },
                new Slice { Label = "right", Action = new NestedRingAction { RingId = _child.Id } },
                new Slice { Label = "down", Action = new PasteTextAction() },
                new Slice { Label = "left", Action = new PasteTextAction() },
            },
        };
        _s = new RingSession(id => id == _child.Id ? _child : null, () => _now);
        _s.Open(_root, 500, 500);
    }

    [Fact]
    public void RestingOnANestedSliceOpensItsRing()
    {
        Assert.Equal(PointerChange.None, _s.PointerMoved(600, 500));
        _now += 100;
        Assert.Equal(PointerChange.None, _s.PointerMoved(600, 500));      // not yet
        _now += 150;
        Assert.Equal(PointerChange.Entered, _s.PointerMoved(600, 500));
        Assert.Same(_child, _s.Current);
        Assert.Same(_root, Assert.Single(_s.Trail).Ring);
    }

    [Fact]
    public void PassingOverANestedSliceQuicklyDoesNot()
    {
        _s.PointerMoved(600, 500);
        _now += 150;
        _s.PointerMoved(500, 600);                                          // on to "down" before the pause is up
        _now += 300;
        Assert.Equal(PointerChange.None, _s.PointerMoved(500, 600));
        Assert.Same(_root, _s.Current);
    }

    [Fact]
    public void MovingOntoAnotherSliceOfTheParentFoldsTheChildAway()
    {
        _s.PointerMoved(600, 500);
        _now += 250;
        _s.PointerMoved(600, 500);
        Assert.Same(_child, _s.Current);
        Assert.Equal(PointerChange.None, _s.PointerMoved(600, 500));       // still on "right": the child stays
        Assert.Equal(PointerChange.WentBack, _s.PointerMoved(500, 400));   // over "up"
        Assert.Same(_root, _s.Current);
        Assert.Equal(0, _s.Selected);
        Assert.Equal(ChooseKind.Run, _s.Choose(500, 400).Kind);
    }

    [Fact]
    public void ComingBackDoesNotReopenUntilThePointerMovesOff()
    {
        _s.PointerMoved(600, 500);
        _now += 250;
        _s.PointerMoved(600, 500);
        _s.Back();                                                          // right click, pointer still on "right"
        _now += 1000;
        Assert.Equal(PointerChange.None, _s.PointerMoved(600, 500));
        _s.PointerMoved(500, 400);
        _s.PointerMoved(600, 500);
        _now += 250;
        Assert.Equal(PointerChange.Entered, _s.PointerMoved(600, 500));
    }

    [Fact]
    public void TheMiddleOfAnEarlierRingAlsoGoesBack()
    {
        _s.PointerMoved(600, 500);
        _now += 250;
        _s.PointerMoved(600, 500);
        Assert.Equal(PointerChange.WentBack, _s.PointerMoved(505, 500));
        Assert.Same(_root, _s.Current);
        Assert.Equal(-1, _s.Selected);
    }

    [Fact]
    public void HoverOpeningCanBeTurnedOff()
    {
        _s.HoverOpens = false;
        _s.PointerMoved(600, 500);
        _now += 5000;
        Assert.Equal(PointerChange.None, _s.PointerMoved(600, 500));
    }

    [Fact]
    public void ANestedSliceWithAWaitOpensOnlyOnceItsCircleIsFull()
    {
        _root.Slices[1].ConfirmMs = 1000;
        _s.PointerMoved(600, 500);
        _now += 500;
        Assert.Equal(PointerChange.None, _s.PointerMoved(600, 500));        // rested long enough to hover-open, not to confirm
        Assert.Equal(PointerChange.None, _s.PointerMoved(500 + 171, 500));  // pushing past doesn't skip the wait either
        Assert.Equal(ChooseKind.NotReady, _s.Choose(600, 500).Kind);        // nor does a click
        Assert.Same(_root, _s.Current);
        _now += 500;
        Assert.Equal(PointerChange.Entered, _s.PointerMoved(600, 500));
        Assert.Same(_child, _s.Current);
    }
}
