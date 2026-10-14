namespace RingDeck.Core;

/// <summary>Angles are degrees, 0 at 12 o'clock, clockwise, with y growing downward.</summary>
public static class RingGeometry
{
    public static double AngleOf(double dx, double dy) => (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;

    public static double SliceAngle(int index, int count) => count <= 0 ? 0 : index * 360.0 / count;

    /// <summary>Index of the slice in the pointer's direction, or -1 inside the dead zone or for an empty ring.</summary>
    public static int SliceAt(double dx, double dy, int count, double deadZone)
    {
        if (count <= 0 || dx * dx + dy * dy < deadZone * deadZone) return -1;
        var step = 360.0 / count;
        return (int)Math.Floor((AngleOf(dx, dy) + step / 2) / step) % count;
    }

    /// <summary>The middle of a screen area, in the same pixels.</summary>
    public static (int X, int Y) Centre(int left, int top, int right, int bottom) => ((left + right) / 2, (top + bottom) / 2);

    public static (double X, double Y) PointAt(double angle, double radius)
    {
        var a = angle * Math.PI / 180;
        return (Math.Sin(a) * radius, -Math.Cos(a) * radius);
    }

    /// <summary>The requested radius, grown so that neighbouring slices keep a small gap.</summary>
    public static double EffectiveRadius(double radius, int count, double sliceSize) =>
        Math.Max(radius, count * sliceSize * 1.15 / (2 * Math.PI));

    /// <summary>Moves a ring centre so a square of half-size <paramref name="extent"/> stays inside the bounds.</summary>
    public static (double X, double Y) ClampCenter(double x, double y, double extent,
        double left, double top, double right, double bottom)
    {
        static double Fit(double v, double lo, double hi, double e) =>
            hi - lo < 2 * e ? (lo + hi) / 2 : Math.Clamp(v, lo + e, hi - e);
        return (Fit(x, left, right, extent), Fit(y, top, bottom, extent));
    }
}
