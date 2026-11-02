namespace RingDeck.Core.Tests;

public class FlowSchedulerTests
{
    static AppConfig MakeConfig(FlowSchedule schedule, string shortcut = "Ctrl+Alt+Space")
    {
        var slice = new Slice { Label = "flow", Action = new FlowAction { Schedule = schedule } };
        return new AppConfig { Rings = { new Ring { Shortcut = shortcut, Slices = { slice } } } };
    }

    [Fact]
    public void DueWhenTimeAndDayMatch()
    {
        var config = MakeConfig(new FlowSchedule { Enabled = true, HourOfDay = 9, MinuteOfDay = 0, Days = ScheduleDays.Monday });
        var due = FlowScheduler.Due(config, new DateTime(2026, 11, 2, 9, 0, 0));   // a Monday
        Assert.Single(due);
        Assert.Same(config.Rings[0].Slices[0], due[0].Slice);
    }

    [Fact]
    public void NotDueTwiceInTheSameMinuteAcrossTicks()
    {
        var config = MakeConfig(new FlowSchedule { Enabled = true, HourOfDay = 9, MinuteOfDay = 0, Days = ScheduleDays.Monday });
        var now = new DateTime(2026, 11, 2, 9, 0, 5);
        Assert.Single(FlowScheduler.Due(config, now));
        Assert.Empty(FlowScheduler.Due(config, now.AddSeconds(20)));   // next tick, same minute
    }

    [Fact]
    public void NotDueOnAWeekdayNotInTheSchedule()
    {
        var config = MakeConfig(new FlowSchedule { Enabled = true, HourOfDay = 9, MinuteOfDay = 0, Days = ScheduleDays.Friday });
        Assert.Empty(FlowScheduler.Due(config, new DateTime(2026, 11, 2, 9, 0, 0)));   // a Monday, not Friday
    }

    [Fact]
    public void DisabledScheduleIsNeverDue()
    {
        var config = MakeConfig(new FlowSchedule { Enabled = false, HourOfDay = 9, MinuteOfDay = 0, Days = ScheduleDays.Monday });
        Assert.Empty(FlowScheduler.Due(config, new DateTime(2026, 11, 2, 9, 0, 0)));
    }

    [Fact]
    public void StaleLastRunDateFromAnEarlierDayDoesNotBlockToday()
    {
        var config = MakeConfig(new FlowSchedule { Enabled = true, HourOfDay = 9, MinuteOfDay = 0, Days = ScheduleDays.Monday, LastRunDate = new DateOnly(2026, 10, 26) });
        Assert.Single(FlowScheduler.Due(config, new DateTime(2026, 11, 2, 9, 0, 0)));
    }

    [Fact]
    public void FlowReachableOnlyAsANestedTargetIsStillFound()
    {
        var config = MakeConfig(new FlowSchedule { Enabled = true, HourOfDay = 9, MinuteOfDay = 0, Days = ScheduleDays.Monday }, shortcut: "");
        Assert.Single(FlowScheduler.Due(config, new DateTime(2026, 11, 2, 9, 0, 0)));
    }

    [Fact]
    public void FlowSliceWithNoScheduleIsNeverDue()
    {
        var config = new AppConfig { Rings = { new Ring { Slices = { new Slice { Action = new FlowAction() } } } } };
        Assert.Empty(FlowScheduler.Due(config, new DateTime(2026, 11, 2, 9, 0, 0)));
    }
}
