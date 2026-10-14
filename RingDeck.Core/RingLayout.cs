namespace RingDeck.Core;

/// <summary>Where a ring's slices sit. A ring opened on its own uses the whole circle. A ring opened from a slice of
/// another ring fans its slices away from that ring, leaving a gap on the side facing back; pointing into the gap
/// means "back".</summary>
public sealed record RingLayout(int Count, double First, double Span)
{
    /// <summary>The gap a nested ring leaves towards the ring it came from, in degrees.</summary>
    public const double BackGap = 100;

    public static RingLayout Full(int count) => new(Math.Max(0, count), 0, 360);

    /// <summary>Slices spread over the circle except a gap centred on <paramref name="back"/> (the direction of the
    /// parent ring).</summary>
    public static RingLayout Fan(int count, double back, double gap = BackGap)
    {
        if (count <= 0) return Full(0);
        var span = 360 - gap;
        return new RingLayout(count, Norm(back + gap / 2 + span / count / 2), span);
    }

    public bool IsFull => Span >= 360;
    public double Step => Count <= 0 ? 360 : Span / Count;
    public double Angle(int index) => Norm(First + index * Step);

    /// <summary>The slice in the pointer's direction, or -1 inside the dead zone, for an empty ring, or in the gap.</summary>
    public int SliceAt(double dx, double dy, double deadZone)
    {
        if (Count <= 0 || dx * dx + dy * dy < deadZone * deadZone) return -1;
        var from = Norm(RingGeometry.AngleOf(dx, dy) - (First - Step / 2));
        if (from >= Span) return -1;
        return Math.Min(Count - 1, (int)Math.Floor(from / Step));
    }

    /// <summary>True when the pointer is outside the dead zone and in the gap towards the parent ring.</summary>
    public bool PointsBack(double dx, double dy, double deadZone) =>
        !IsFull && Count > 0 && dx * dx + dy * dy >= deadZone * deadZone && SliceAt(dx, dy, deadZone) < 0;

    /// <summary>The radius a ring needs so its slices don't overlap within the span they share.</summary>
    public double RadiusFor(double radius, double sliceSize) =>
        Math.Max(radius, Count * sliceSize * 1.15 / (2 * Math.PI * (IsFull ? 1 : Span / 360)));

    static double Norm(double angle) => (angle % 360 + 360) % 360;
}
