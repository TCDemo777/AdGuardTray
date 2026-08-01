namespace AdGuardTray.Models;

public enum BehaviourObservationSeverity
{
    Information,
    Warning,
    Critical
}

public sealed class BehaviourObservation
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public BehaviourObservationSeverity Severity { get; init; }
    public string Category { get; init; } = string.Empty;
    public int Priority { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}

public sealed class DeviceBehaviourProfile
{
    public string MacAddress { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int? TypicalOnlineHour { get; init; }
    public int? TypicalOfflineHour { get; init; }
    public TimeSpan? AverageSessionDuration { get; init; }
    public int DaysSinceLastSeen { get; init; }
    public double ReconnectsPerWeek { get; init; }
    public string PreferredNetwork { get; init; } = string.Empty;
    public string MostCommonIpAddress { get; init; } = string.Empty;

    public string TypicalOnlineTimeDisplay => TypicalOnlineHour is { } hour
        ? $"{hour:00}:00–{(hour + 1) % 24:00}:00" : "Not enough history";
    public string AverageSessionDisplay => AverageSessionDuration is { } duration
        ? duration.TotalHours >= 1 ? $"{duration.TotalHours:F1} hours" : $"{duration.TotalMinutes:F0} minutes"
        : "Not enough history";
    public string PreferredNetworkDisplay => string.IsNullOrWhiteSpace(PreferredNetwork)
        ? "Not enough history" : PreferredNetwork;
}

public sealed class BehaviourAnalysis
{
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public IReadOnlyList<DeviceHistoryRecord> Devices { get; init; } = Array.Empty<DeviceHistoryRecord>();
    public IReadOnlyList<DeviceConnectionEvent> DeviceEvents { get; init; } = Array.Empty<DeviceConnectionEvent>();
    public IReadOnlyList<WanMinuteSnapshot> WanHistory { get; init; } = Array.Empty<WanMinuteSnapshot>();
    public IReadOnlyList<RouterHealthMinuteSnapshot> RouterHealth { get; init; } = Array.Empty<RouterHealthMinuteSnapshot>();
    public Dictionary<string, DeviceBehaviourProfile> DeviceProfiles { get; } =
        new(StringComparer.OrdinalIgnoreCase);
}
