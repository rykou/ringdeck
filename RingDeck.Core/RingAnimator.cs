namespace RingDeck.Core;

public enum AnimPhase { Hidden, Opening, Open, Closing }

/// <summary>Frame-driven animation state for one ring. Call Tick once per rendered frame.</summary>
public sealed class RingAnimator
{
    public const double SelectedScale = 1.12;
    const double SlowFrame = 0.05;   // below 20 fps: skip animation for that frame

    double _elapsed;
    double[] _scales = Array.Empty<double>();

    public AnimPhase Phase { get; private set; } = AnimPhase.Hidden;
    public RingPose Pose { get; private set; } = RingPose.Rest;
    public double PointerAngle { get; private set; }
    public bool HasPointer { get; private set; }
    public IReadOnlyList<double> SliceScales => _scales;

    public void Open(int count)
    {
        Reset(count);
        Phase = AnimPhase.Opening;
        Pose = Motion.OpenPose(0);
    }

    /// <summary>Shows the ring at rest with no opening motion (editor preview).</summary>
    public void ShowStatic(int count)
    {
        Reset(count);
        Phase = AnimPhase.Open;
        Pose = RingPose.Rest;
    }

    public void Close()
    {
        if (Phase is AnimPhase.Hidden or AnimPhase.Closing) return;
        Phase = AnimPhase.Closing;
        _elapsed = 0;
    }

    void Reset(int count)
    {
        _elapsed = 0;
        HasPointer = false;
        _scales = Enumerable.Repeat(1.0, Math.Max(0, count)).ToArray();
    }

    public void Tick(double dt, double? targetAngle, int selected, bool animate, bool instantPointer, bool spiral)
    {
        if (dt > SlowFrame) animate = false;
        _elapsed += dt;
        switch (Phase)
        {
            case AnimPhase.Opening:
                Pose = animate ? Motion.OpenPose(_elapsed) : RingPose.Rest;
                if (Pose == RingPose.Rest) Phase = AnimPhase.Open;
                break;
            case AnimPhase.Closing:
                Pose = Motion.ClosePose(_elapsed, spiral);
                if (!animate || _elapsed >= Motion.Duration) Phase = AnimPhase.Hidden;
                break;
        }
        if (targetAngle is { } target)
        {
            PointerAngle = HasPointer ? Motion.StepPointer(PointerAngle, target, dt, instantPointer || !animate) : target;
            HasPointer = true;
        }
        for (var i = 0; i < _scales.Length; i++)
            _scales[i] = Motion.StepToward(_scales[i], i == selected ? SelectedScale : 1, dt, animate ? 1.2 : 1000);
    }
}
