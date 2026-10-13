namespace RingDeck.Core.Tests;

public class MotionTests
{
    [Theory]
    [InlineData(10, 30, 20)]
    [InlineData(350, 10, 20)]
    [InlineData(10, 350, -20)]
    [InlineData(0, 180, 180)]
    public void AngleDiffTakesShorterWay(double from, double to, double expected) =>
        Assert.Equal(expected, Motion.AngleDiff(from, to), 6);

    [Fact]
    public void PointerConvergesWithoutOvershoot()
    {
        double angle = 350;
        var previous = Math.Abs(Motion.AngleDiff(angle, 40));
        for (var i = 0; i < 120; i++)
        {
            angle = Motion.StepPointer(angle, 40, 1 / 60.0, false);
            var remaining = Math.Abs(Motion.AngleDiff(angle, 40));
            Assert.True(remaining <= previous + 1e-9);
            previous = remaining;
            Assert.InRange(angle, 0, 360);
        }
        Assert.Equal(40, angle, 6);
    }

    [Fact]
    public void PointerCrossesZeroTheShortWay()
    {
        var next = Motion.StepPointer(350, 40, 1 / 60.0, false);
        Assert.True(next > 350 || next < 40);
    }

    [Fact] public void InstantPointerJumps() => Assert.Equal(200, Motion.StepPointer(10, 200, 0.001, true), 6);

    [Fact]
    public void OpenPoseEndsAtRest()
    {
        Assert.Equal(0, Motion.OpenPose(0).Alpha, 6);
        Assert.True(Motion.OpenPose(0).Scale > 1);
        Assert.Equal(RingPose.Rest, Motion.OpenPose(Motion.Duration));
        Assert.Equal(RingPose.Rest, Motion.OpenPose(99));
    }

    [Fact]
    public void ClosePoseStartsAtRestAndFadesOut()
    {
        Assert.Equal(RingPose.Rest, Motion.ClosePose(0, true));
        var end = Motion.ClosePose(Motion.Duration, true);
        Assert.Equal(0, end.Alpha, 6);
        Assert.True(end.RadiusFactor > 1);
        Assert.True(end.Rotation > 0);
        Assert.True(Motion.ClosePose(Motion.Duration, false).Scale < 1);
    }

    [Fact]
    public void AnimatorRunsThroughPhases()
    {
        var a = new RingAnimator();
        Assert.Equal(AnimPhase.Hidden, a.Phase);
        a.Open(4);
        Assert.Equal(AnimPhase.Opening, a.Phase);
        for (var i = 0; i < 30; i++) a.Tick(1 / 60.0, 90, 1, true, false, true);
        Assert.Equal(AnimPhase.Open, a.Phase);
        Assert.Equal(RingPose.Rest, a.Pose);
        Assert.Equal(90, a.PointerAngle, 6);
        Assert.True(a.SliceScales[1] > 1.05);
        Assert.Equal(1, a.SliceScales[0], 6);
        a.Close();
        for (var i = 0; i < 30; i++) a.Tick(1 / 60.0, null, -1, true, false, true);
        Assert.Equal(AnimPhase.Hidden, a.Phase);
    }

    [Fact]
    public void AnimatorWithAnimationsOffIsImmediate()
    {
        var a = new RingAnimator();
        a.Open(3);
        a.Tick(1 / 60.0, 180, 2, false, false, true);
        Assert.Equal(AnimPhase.Open, a.Phase);
        Assert.Equal(RingPose.Rest, a.Pose);
        a.Close();
        a.Tick(1 / 60.0, null, -1, false, false, true);
        Assert.Equal(AnimPhase.Hidden, a.Phase);
    }

    [Fact]
    public void SlowFrameSkipsAnimation()
    {
        var a = new RingAnimator();
        a.Open(3);
        a.Tick(0.2, null, -1, true, false, true);
        Assert.Equal(AnimPhase.Open, a.Phase);
    }

    [Fact]
    public void SelectedIndexOutsideRangeIsIgnored()
    {
        var a = new RingAnimator();
        a.Open(2);
        a.Tick(1 / 60.0, 0, 7, true, false, true);
        Assert.Equal(2, a.SliceScales.Count);
    }
}
