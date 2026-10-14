namespace RingDeck.Core.Tests;

public class HoldToConfirmTests
{
    long _now;
    readonly Ring _ring = new()
    {
        Slices =
        {
            new Slice { Label = "Send", ConfirmMs = 1000, Action = new WebhookAction() },   // up
            new Slice { Label = "Paste", Action = new PasteTextAction() },                  // right
        },
    };
    readonly RingSession _s;
    const double Up = -120, Right = 120;

    public HoldToConfirmTests()
    {
        _s = new RingSession(_ => null, () => _now);
        _s.Open(_ring, 0, 0);
    }

    [Fact]
    public void LettingGoBeforeTheCircleFillsCancels()
    {
        _s.PointerMoved(0, Up);
        _now += 400;
        Assert.Equal(0.4, _s.HoldProgress, 3);
        Assert.Equal(new ChooseResult(ChooseKind.Closed, null), _s.Release(0, Up));
        Assert.False(_s.IsOpen);
    }

    [Fact]
    public void AnEarlyClickLeavesTheRingOpenToTryAgain()
    {
        _ring.Mode = OpenMode.Click;
        _s.Open(_ring, 0, 0);
        _s.PointerMoved(0, Up);
        _now += 400;
        Assert.Equal(ChooseKind.NotReady, _s.Choose(0, Up).Kind);
        Assert.True(_s.IsOpen);
        _now += 600;
        Assert.Equal(ChooseKind.Run, _s.Choose(0, Up).Kind);
    }

    [Fact]
    public void AfterTheWaitItRunsOnRelease()
    {
        _s.PointerMoved(0, Up);
        _now += 1000;
        Assert.Equal(ChooseKind.Run, _s.Release(0, Up).Kind);
    }

    [Fact]
    public void MovingAwayStartsTheWaitAgain()
    {
        _s.PointerMoved(0, Up);
        _now += 900;
        _s.PointerMoved(Right, 0);
        _s.PointerMoved(0, Up);
        _now += 200;
        Assert.Equal(0.2, _s.HoldProgress, 3);
        Assert.Equal(ChooseKind.NotReady, _s.Choose(0, Up).Kind);
    }

    [Fact]
    public void JumpingStraightToTheSliceAndClickingStillWaits()
    {
        // a click with no pointer movement before it: the timer starts at the click, so it is too soon
        Assert.Equal(ChooseKind.NotReady, _s.Choose(0, Up).Kind);
    }

    [Fact]
    public void SlicesWithoutAWaitRunAtOnce()
    {
        _s.PointerMoved(Right, 0);
        Assert.Equal(1, _s.HoldProgress);
        Assert.Equal(ChooseKind.Run, _s.Release(Right, 0).Kind);
    }

    [Fact]
    public void TheWaitIsKeptWithinFiveSeconds()
    {
        var config = ConfigStore.FromJson("""{ "rings": [ { "slices": [ { "confirmMs": 99999 }, { "confirmMs": -5 } ] } ] }""")!;
        Assert.Equal(5000, config.Rings[0].Slices[0].ConfirmMs);
        Assert.Equal(0, config.Rings[0].Slices[1].ConfirmMs);
    }
}
