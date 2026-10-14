namespace RingDeck.Core.Tests;

public class CropMathTests
{
    const double View = 300;

    [Fact]
    public void StartsCenteredAndJustCoveringTheView()
    {
        var v = CropMath.Start(800, 400, View);            // landscape: the short side (400) fills the view
        Assert.Equal(0.75, v.Zoom, 6);
        Assert.Equal((300 - 600) / 2.0, v.X, 6);           // 800 * 0.75 = 600 wide, centred
        Assert.Equal(0, v.Y, 6);
    }

    [Fact]
    public void PanCannotUncoverTheView()
    {
        var v = CropMath.Start(800, 400, View);
        var right = CropMath.Pan(v, 10_000, 0, 800, 400, View);
        Assert.Equal(0, right.X, 6);                       // left edge stops at the view's left edge
        var left = CropMath.Pan(v, -10_000, 0, 800, 400, View);
        Assert.Equal(View - 600, left.X, 6);               // right edge stops at the view's right edge
        Assert.Equal(0, CropMath.Pan(v, 0, 50, 800, 400, View).Y, 6);   // no room vertically
    }

    [Fact]
    public void ZoomKeepsThePointUnderThePointerStill()
    {
        var v = CropMath.Start(1000, 1000, View);          // zoom 0.3
        var z = CropMath.ZoomAt(v, 0.6, 100, 150, 1000, 1000, View);
        Assert.Equal(0.6, z.Zoom, 6);
        // the image point that was under (100,150) is still there
        Assert.Equal((100 - v.X) / v.Zoom, (100 - z.X) / z.Zoom, 6);
        Assert.Equal((150 - v.Y) / v.Zoom, (150 - z.Y) / z.Zoom, 6);
    }

    [Fact]
    public void ZoomIsLimitedBetweenCoverAndEightTimesThat()
    {
        var v = CropMath.Start(1000, 1000, View);
        Assert.Equal(0.3, CropMath.ZoomAt(v, 0.01, 150, 150, 1000, 1000, View).Zoom, 6);
        Assert.Equal(2.4, CropMath.ZoomAt(v, 99, 150, 150, 1000, 1000, View).Zoom, 6);
        var tiny = CropMath.Start(32, 32, View);           // small icons may be enlarged up to the limit too
        Assert.Equal(View / 32 * 8, CropMath.ZoomAt(tiny, 1000, 0, 0, 32, 32, View).Zoom, 6);
    }

    [Fact]
    public void ZoomingOutNearAnEdgeStaysCovered()
    {
        var v = CropMath.ZoomAt(CropMath.Start(1000, 1000, View), 2.4, 300, 300, 1000, 1000, View);
        var back = CropMath.ZoomAt(v, 0.3, 0, 0, 1000, 1000, View);
        Assert.Equal(0, back.X, 6);
        Assert.Equal(0, back.Y, 6);
    }

    [Fact]
    public void SourceSquareIsWhatTheViewShows()
    {
        var v = CropMath.Pan(CropMath.ZoomAt(CropMath.Start(1000, 1000, View), 0.6, 150, 150, 1000, 1000, View), -30, -60, 1000, 1000, View);
        var (x, y, size) = CropMath.SourceSquare(v, View);
        Assert.Equal(View / 0.6, size, 6);
        Assert.Equal(-v.X / v.Zoom, x, 6);
        Assert.Equal(-v.Y / v.Zoom, y, 6);
        Assert.True(x >= 0 && y >= 0 && x + size <= 1000 + 1e-9 && y + size <= 1000 + 1e-9);
    }
}
