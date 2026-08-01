using AdGuardTray.Models;

namespace AdGuardTray.Services;

public sealed class AdGuardServiceScheduleCalculator(IClock clock)
{
    public DateTimeOffset? Next(AdGuardServiceSchedule schedule, DateTimeOffset afterUtc)
    {
        TimeZoneInfo zone = clock.LocalTimeZone;
        DateTime localAfter = TimeZoneInfo.ConvertTime(afterUtc, zone).DateTime;
        DateOnly start = DateOnly.FromDateTime(localAfter);
        for (int offset = 0; offset <= 370; offset++)
        {
            DateOnly date = start.AddDays(offset);
            if (!OccursOn(schedule, date)) continue;
            DateTimeOffset occurrence = ResolveLocal(date, schedule.LocalTime, zone);
            if (occurrence.ToUniversalTime() > afterUtc.ToUniversalTime()) return occurrence;
        }
        return null;
    }

    public DateTimeOffset? DueOccurrence(AdGuardServiceSchedule schedule, DateTimeOffset nowUtc, TimeSpan grace)
    {
        DateTimeOffset lower = nowUtc - grace;
        DateTimeOffset? candidate = Next(schedule, lower.AddTicks(-1));
        return candidate is not null && candidate.Value.ToUniversalTime() <= nowUtc.ToUniversalTime() ? candidate : null;
    }

    private static bool OccursOn(AdGuardServiceSchedule schedule, DateOnly date) => schedule.Recurrence switch
    {
        AdGuardServiceScheduleRecurrence.Once => schedule.OneTimeDate == date,
        AdGuardServiceScheduleRecurrence.Daily => true,
        _ => (schedule.SelectedDays & ToFlag(date.DayOfWeek)) != 0
    };

    private static ScheduleDays ToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => ScheduleDays.Monday, DayOfWeek.Tuesday => ScheduleDays.Tuesday,
        DayOfWeek.Wednesday => ScheduleDays.Wednesday, DayOfWeek.Thursday => ScheduleDays.Thursday,
        DayOfWeek.Friday => ScheduleDays.Friday, DayOfWeek.Saturday => ScheduleDays.Saturday,
        _ => ScheduleDays.Sunday
    };

    private static DateTimeOffset ResolveLocal(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        DateTime local = date.ToDateTime(time, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        TimeSpan offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
