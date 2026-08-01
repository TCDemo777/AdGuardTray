using System.Text.Json.Serialization;

namespace AdGuardTray.Models;

public enum AdGuardServiceScheduleAction { Allow, Block }
public enum AdGuardServiceScheduleRecurrence { Once, Daily, SelectedDays }

[Flags]
public enum ScheduleDays
{
    None = 0, Monday = 1, Tuesday = 2, Wednesday = 4, Thursday = 8,
    Friday = 16, Saturday = 32, Sunday = 64, Weekdays = 31, Weekend = 96, All = 127
}

public sealed class AdGuardServiceSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> ServiceIds { get; set; } = [];
    public AdGuardServiceScheduleAction Action { get; set; }
    public TimeOnly LocalTime { get; set; }
    public AdGuardServiceScheduleRecurrence Recurrence { get; set; } = AdGuardServiceScheduleRecurrence.Daily;
    public ScheduleDays SelectedDays { get; set; } = ScheduleDays.All;
    public DateOnly? OneTimeDate { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset? LastExecutedUtc { get; set; }
    public DateTimeOffset? LastAttemptedOccurrenceUtc { get; set; }
    public DateTimeOffset? NextExecutionLocal { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? LastError { get; set; }
    public DateTimeOffset? LastErrorUtc { get; set; }

    [JsonIgnore] public string ServiceDisplay { get; set; } = string.Empty;
    [JsonIgnore] public string RecurrenceDisplay => Recurrence switch
    {
        AdGuardServiceScheduleRecurrence.Once => OneTimeDate?.ToString("dd MMM yyyy") ?? "Once",
        AdGuardServiceScheduleRecurrence.Daily => "Daily",
        _ => SelectedDays.ToString().Replace(",", " ·")
    };
    [JsonIgnore] public string NextExecutionDisplay => NextExecutionLocal?.ToString("ddd dd MMM, HH:mm") ?? "No upcoming run";
    [JsonIgnore] public string LastResultDisplay => LastError is not null ? LastError : LastExecutedUtc is not null ? $"Completed {LastExecutedUtc.Value.ToLocalTime():dd MMM HH:mm}" : "Not run yet";
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    TimeZoneInfo LocalTimeZone { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
}
