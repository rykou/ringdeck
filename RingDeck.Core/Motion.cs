namespace RingDeck.Core;

/// <summary>How a ring is drawn at one instant: overall scale and opacity, extra rotation of the slices (degrees),
/// and a multiplier on the slice circle's radius.</summary>
public readonly record struct RingPose(double Scale, double Alpha, double Rotation, double RadiusFactor)
{
    public static readonly RingPose Rest = new(1, 1, 0, 1);
}

public static class Motion
{
    public const double Duration = 0.25;

    /// <summary>Signed shortest rotation from one angle to another, in (-180, 180].</summary>
    public static double AngleDiff(double from, double to)
    {
        var d = (to - from) % 360;
        if (d > 180) d -= 360;
        if (d <= -180) d += 360;
        return d;
    }

    static double Norm(double a) => (a % 360 + 360) % 360;

    /// <summary>Moves the pointer toward the target: fast when far, easing as it arrives, never overshooting.</summary>
    public static double StepPointer(double current, double target, double dt, bool instant)
    {
        var d = AngleDiff(current, target);
        var remaining = Math.Abs(d);
        if (instant) return Norm(target);
        var step = Math.Max(120, remaining * 14) * dt;
        return step >= remaining ? Norm(target) : Norm(current + Math.Sign(d) * step);
    }

    static double EaseOut(double p) { var q = 1 - Math.Clamp(p, 0, 1); return 1 - q * q * q; }

    /// <summary>Open: settle from enlarged and rotated back to rest while fading in.</summary>
    public static RingPose OpenPose(double elapsed)
    {
        var e = EaseOut(elapsed / Duration);
        return e >= 1 ? RingPose.Rest : new RingPose(1.18 - 0.18 * e, e, -35 * (1 - e), 0.55 + 0.45 * e);
    }

    /// <summary>Close: spiral outward and fade, or shrink and fade when <paramref name="spiral"/> is off.</summary>
    public static RingPose ClosePose(double elapsed, bool spiral)
    {
        var p = Math.Clamp(elapsed / Duration, 0, 1);
        if (p <= 0) return RingPose.Rest;
        var e = p * p;
        return spiral ? new RingPose(1 + 0.12 * e, 1 - p, 50 * e, 1 + 0.45 * e) : new RingPose(1 - 0.6 * e, 1 - p, 0, 1);
    }

    public static double StepToward(double current, double target, double dt, double rate)
    {
        var d = target - current;
        var step = rate * dt;
        return Math.Abs(d) <= step ? target : current + Math.Sign(d) * step;
    }
}
