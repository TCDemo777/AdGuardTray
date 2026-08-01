using System.Text.Json.Serialization;

namespace AdGuardTray.Models;

public sealed class AdGuardServiceWindow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AllowScheduleId { get; set; } = Guid.NewGuid();
    public Guid BlockScheduleId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<string> ServiceIds { get; set; } = [];
    public TimeOnly AllowTime { get; set; }
    public TimeOnly BlockTime { get; set; }
    public AdGuardServiceScheduleRecurrence Recurrence { get; set; } = AdGuardServiceScheduleRecurrence.Daily;
    public ScheduleDays SelectedDays { get; set; } = ScheduleDays.All;
    public DateOnly? OneTimeDate { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastActionUtc { get; set; }
    public string? LastResult { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? NextExecutionLocal { get; set; }
    public AdGuardServiceScheduleAction? NextAction { get; set; }

    [JsonIgnore] public string ServiceDisplay { get; set; } = string.Empty;
    [JsonIgnore] public bool CrossesMidnight => BlockTime <= AllowTime;
    [JsonIgnore] public string TimeRangeDisplay => $"Allowed from {AllowTime:HH:mm} until {BlockTime:HH:mm}{(CrossesMidnight ? " the following day" : string.Empty)}";
    [JsonIgnore] public string RecurrenceDisplay => Recurrence switch
    {
        AdGuardServiceScheduleRecurrence.Once => OneTimeDate?.ToString("dd MMM yyyy") ?? "Once",
        AdGuardServiceScheduleRecurrence.Daily => "Daily",
        _ => SelectedDays.ToString().Replace(",", " ·")
    };
    [JsonIgnore] public string NextActionDisplay => NextExecutionLocal is null || NextAction is null
        ? "No upcoming action"
        : $"{NextAction} {NextExecutionLocal.Value:ddd dd MMM 'at' HH:mm}";
    [JsonIgnore] public string LastResultDisplay => LastError ?? LastResult ?? "Not run yet";
}
