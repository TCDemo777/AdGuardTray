namespace AdGuardTray.Models;

public enum TimelineCategory
{
    Devices,
    Router,
    AdGuard,
    Insights,
    Firmware
}

public enum TimelineSeverity
{
    Information,
    Success,
    Warning,
    Error,
    Critical
}

public enum TimelineFilter
{
    All,
    Devices,
    Router,
    AdGuard,
    Insights
}

public sealed class TimelineEvent
{
    public required string SourceId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public TimelineCategory Category { get; init; }
    public TimelineSeverity Severity { get; init; }

    public string TimeDisplay => Timestamp.ToLocalTime().ToString("dd MMM yyyy HH:mm");
    public string Icon => Category switch
    {
        TimelineCategory.Devices => "D",
        TimelineCategory.Router => "R",
        TimelineCategory.AdGuard => "A",
        TimelineCategory.Insights => "i",
        TimelineCategory.Firmware => "F",
        _ => "•"
    };
}
