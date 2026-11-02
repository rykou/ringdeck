namespace RingDeck.Core;

/// <summary>Finds flow slices whose schedule is due right now, and marks them as having run today.</summary>
public static class FlowScheduler
{
    public static IReadOnlyList<(Ring Ring, Slice Slice)> Due(AppConfig config, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var day = ToFlag(now.DayOfWeek);
        var due = new List<(Ring, Slice)>();
        foreach (var ring in config.Rings)
            foreach (var slice in ring.Slices)
                if (slice.Action is FlowAction { Schedule: { Enabled: true } schedule }
                    && schedule.Days.HasFlag(day)
                    && schedule.HourOfDay == now.Hour && schedule.MinuteOfDay == now.Minute
                    && schedule.LastRunDate != today)
                {
                    schedule.LastRunDate = today;
                    due.Add((ring, slice));
                }
        return due;
    }

    static ScheduleDays ToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => ScheduleDays.Monday,
        DayOfWeek.Tuesday => ScheduleDays.Tuesday,
        DayOfWeek.Wednesday => ScheduleDays.Wednesday,
        DayOfWeek.Thursday => ScheduleDays.Thursday,
        DayOfWeek.Friday => ScheduleDays.Friday,
        DayOfWeek.Saturday => ScheduleDays.Saturday,
        _ => ScheduleDays.Sunday,
    };
}
