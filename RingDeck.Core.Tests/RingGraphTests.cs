namespace RingDeck.Core.Tests;

public class RingGraphTests
{
    static Ring Linked(string name, params Ring[] children)
    {
        var ring = new Ring { Name = name };
        foreach (var c in children) ring.Slices.Add(new Slice { Action = new NestedRingAction { RingId = c.Id } });
        return ring;
    }

    [Fact]
    public void DetectsDirectAndIndirectCycles()
    {
        var c = Linked("c");
        var b = Linked("b", c);
        var a = Linked("a", b);
        var config = new AppConfig { Rings = { a, b, c } };
        Assert.True(RingGraph.Reaches(config, a.Id, c.Id));
        Assert.False(RingGraph.Reaches(config, c.Id, a.Id));
        Assert.True(RingGraph.WouldCycle(config, a.Id, a.Id));
        Assert.True(RingGraph.WouldCycle(config, c.Id, a.Id));
        Assert.False(RingGraph.WouldCycle(config, a.Id, c.Id));
    }

    [Fact]
    public void ExistingCycleDoesNotHang()
    {
        var a = new Ring(); var b = new Ring();
        a.Slices.Add(new Slice { Action = new NestedRingAction { RingId = b.Id } });
        b.Slices.Add(new Slice { Action = new NestedRingAction { RingId = a.Id } });
        var config = new AppConfig { Rings = { a, b } };
        Assert.False(RingGraph.Reaches(config, a.Id, Guid.NewGuid()));
    }
}
