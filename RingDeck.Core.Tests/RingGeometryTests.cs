namespace RingDeck.Core.Tests;

public class RingGeometryTests
{
    [Theory]
    [InlineData(0, -10, 0)]
    [InlineData(10, 0, 90)]
    [InlineData(0, 10, 180)]
    [InlineData(-10, 0, 270)]
    public void AngleIsClockwiseFromTop(double dx, double dy, double expected) =>
        Assert.Equal(expected, RingGeometry.AngleOf(dx, dy), 6);

    [Fact]
    public void SliceZeroIsCentredAtTop()
    {
        Assert.Equal(0, RingGeometry.SliceAt(0, -100, 4, 20));
        Assert.Equal(1, RingGeometry.SliceAt(100, 0, 4, 20));
        Assert.Equal(2, RingGeometry.SliceAt(0, 100, 4, 20));
        Assert.Equal(3, RingGeometry.SliceAt(-100, 0, 4, 20));
    }

    [Fact]
    public void BoundaryBetweenSlicesIsHalfway()
    {
        var justBefore = RingGeometry.PointAt(44.9, 100);
        var justAfter = RingGeometry.PointAt(45.1, 100);
        Assert.Equal(0, RingGeometry.SliceAt(justBefore.X, justBefore.Y, 4, 20));
        Assert.Equal(1, RingGeometry.SliceAt(justAfter.X, justAfter.Y, 4, 20));
        var wrap = RingGeometry.PointAt(359, 100);
        Assert.Equal(0, RingGeometry.SliceAt(wrap.X, wrap.Y, 4, 20));
    }

    [Fact]
    public void DeadZoneAndEmptyRingSelectNothing()
    {
        Assert.Equal(-1, RingGeometry.SliceAt(5, 5, 4, 20));
        Assert.Equal(-1, RingGeometry.SliceAt(0, -100, 0, 20));
    }

    [Fact]
    public void SingleSliceTakesEveryDirection()
    {
        Assert.Equal(0, RingGeometry.SliceAt(0, 100, 1, 20));
        Assert.Equal(0, RingGeometry.SliceAt(-100, 0, 1, 20));
    }

    [Fact]
    public void DistanceBeyondRingStillSelects() => Assert.Equal(1, RingGeometry.SliceAt(5000, 0, 4, 20));

    [Fact]
    public void RadiusGrowsWhenSlicesWouldOverlap()
    {
        Assert.Equal(120, RingGeometry.EffectiveRadius(120, 6, 56));
        var big = RingGeometry.EffectiveRadius(120, 40, 56);
        Assert.True(big > 120);
        Assert.True(2 * Math.PI * big / 40 >= 56);
    }

    [Fact]
    public void ClampKeepsRingOnScreen()
    {
        Assert.Equal((150.0, 150.0), RingGeometry.ClampCenter(10, 20, 150, 0, 0, 1920, 1080));
        Assert.Equal((1770.0, 930.0), RingGeometry.ClampCenter(1900, 1070, 150, 0, 0, 1920, 1080));
        Assert.Equal((500.0, 500.0), RingGeometry.ClampCenter(500, 500, 150, 0, 0, 1920, 1080));
        Assert.Equal((100.0, 100.0), RingGeometry.ClampCenter(30, 30, 150, 0, 0, 200, 200));   // screen smaller than ring: centre it
    }
}
