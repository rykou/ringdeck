namespace RingDeck.Core.Tests;

public class FlowScheduleTests
{
    [Fact]
    public void ScheduleRoundTripsThroughJson()
    {
        var config = new AppConfig { Rings = { new Ring { Slices = { new Slice { Action = new FlowAction
        {
            Schedule = new FlowSchedule { Enabled = true, HourOfDay = 8, MinuteOfDay = 30, Days = ScheduleDays.Monday | ScheduleDays.Friday, LastRunDate = new DateOnly(2026, 10, 30) },
        } } } } } };
        var loaded = ConfigStore.FromJson(ConfigStore.ToJson(config))!;
        var schedule = ((FlowAction)loaded.Rings[0].Slices[0].Action!).Schedule!;
        Assert.True(schedule.Enabled);
        Assert.Equal(8, schedule.HourOfDay);
        Assert.Equal(30, schedule.MinuteOfDay);
        Assert.Equal(ScheduleDays.Monday | ScheduleDays.Friday, schedule.Days);
        Assert.Equal(new DateOnly(2026, 10, 30), schedule.LastRunDate);
    }

    [Fact]
    public void FlowWithNoScheduleRoundTripsAsNull()
    {
        var config = new AppConfig { Rings = { new Ring { Slices = { new Slice { Action = new FlowAction() } } } } };
        var loaded = ConfigStore.FromJson(ConfigStore.ToJson(config))!;
        Assert.Null(((FlowAction)loaded.Rings[0].Slices[0].Action!).Schedule);
    }

    [Fact]
    public void OutOfRangeScheduleIsClamped()
    {
        var config = new AppConfig { Rings = { new Ring { Slices = { new Slice { Action = new FlowAction
        {
            Schedule = new FlowSchedule { Enabled = true, HourOfDay = 99, MinuteOfDay = -5, Days = (ScheduleDays)255 },
        } } } } } };
        var loaded = ConfigStore.FromJson(ConfigStore.ToJson(config))!;
        var schedule = ((FlowAction)loaded.Rings[0].Slices[0].Action!).Schedule!;
        Assert.Equal(23, schedule.HourOfDay);
        Assert.Equal(0, schedule.MinuteOfDay);
        Assert.Equal((ScheduleDays)127, schedule.Days);
    }
}
