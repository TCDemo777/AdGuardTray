using AdGuardTray.Models;

namespace AdGuardTray.Services;

public sealed class TimelineService
{
    private readonly HistoryRepository _historyRepository;
    private readonly NotificationService _notificationService;
    private readonly object _insightLock = new();
    private readonly Dictionary<string, TimelineEvent> _insightEvents =
        new(StringComparer.Ordinal);

    public TimelineService(
        HistoryRepository historyRepository,
        NotificationService notificationService)
    {
        _historyRepository = historyRepository;
        _notificationService = notificationService;
    }

    public void RecordInsights(IEnumerable<Insight> insights)
    {
        lock (_insightLock)
        {
            foreach (Insight insight in insights)
            {
                string key = $"insight:{insight.Category}:{insight.Title}";
                if (_insightEvents.ContainsKey(key))
                    continue;

                _insightEvents[key] = new TimelineEvent
                {
                    SourceId = key,
                    Timestamp = insight.Timestamp,
                    Title = insight.Title,
                    Description = insight.Description,
                    Category = TimelineCategory.Insights,
                    Severity = insight.Severity switch
                    {
                        InsightSeverity.Critical => TimelineSeverity.Critical,
                        InsightSeverity.Warning => TimelineSeverity.Warning,
                        _ => TimelineSeverity.Information
                    }
                };
            }
        }
    }

    public async Task<IReadOnlyList<TimelineEvent>> GetEventsAsync(
        int offset,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || maximumCount <= 0)
            return Array.Empty<TimelineEvent>();

        int sourceLimit = Math.Min(offset + maximumCount + 100, 1000);
        IReadOnlyList<DeviceConnectionEvent> deviceEvents =
            await _historyRepository.GetRecentDeviceEventsAsync(
                sourceLimit, cancellationToken).ConfigureAwait(false);

        AppNotification[] notifications = _notificationService.Notifications
            .Take(sourceLimit)
            .ToArray();
        TimelineEvent[] insights;
        lock (_insightLock)
            insights = _insightEvents.Values.ToArray();

        return deviceEvents.Select(MapDeviceEvent)
            .Concat(notifications.Select(MapNotification))
            .Concat(insights)
            .OrderByDescending(item => item.Timestamp)
            .ThenBy(item => item.SourceId, StringComparer.Ordinal)
            .GroupBy(CreateDeduplicationKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Skip(offset)
            .Take(maximumCount)
            .ToArray();
    }

    private static TimelineEvent MapDeviceEvent(DeviceConnectionEvent item)
    {
        string name = FirstValue(item.FriendlyName, item.Hostname, item.MacAddress);
        return new TimelineEvent
        {
            SourceId = "device:" + item.Id,
            Timestamp = item.TimestampUtc,
            Category = TimelineCategory.Devices,
            Severity = item.EventType switch
            {
                DeviceConnectionEventType.Disconnected => TimelineSeverity.Warning,
                DeviceConnectionEventType.Connected => TimelineSeverity.Success,
                _ => TimelineSeverity.Information
            },
            Title = item.EventType switch
            {
                DeviceConnectionEventType.FirstSeen => "New Device",
                DeviceConnectionEventType.Connected => "Device Connected",
                DeviceConnectionEventType.Disconnected => "Device Disconnected",
                DeviceConnectionEventType.IpChanged => "IP Address Changed",
                DeviceConnectionEventType.NetworkChanged => "Network Changed",
                _ => "Device Event"
            },
            Description = item.EventType switch
            {
                DeviceConnectionEventType.FirstSeen => $"{name} was seen for the first time.",
                DeviceConnectionEventType.Connected => $"{name} connected.",
                DeviceConnectionEventType.Disconnected => $"{name} disconnected.",
                DeviceConnectionEventType.IpChanged => $"{name}: {item.IpAddress}",
                DeviceConnectionEventType.NetworkChanged => $"{name}: {item.NetworkName}",
                _ => name
            }
        };
    }

    private static TimelineEvent MapNotification(AppNotification item) => new()
    {
        SourceId = "notification:" + item.Id,
        Timestamp = item.Timestamp,
        Title = item.Title,
        Description = item.Message,
        Category = item.Category switch
        {
            NotificationCategory.Device => TimelineCategory.Devices,
            NotificationCategory.Router or NotificationCategory.Internet => TimelineCategory.Router,
            NotificationCategory.AdGuard => TimelineCategory.AdGuard,
            _ => TimelineCategory.Insights
        },
        Severity = item.Severity switch
        {
            NotificationSeverity.Success => TimelineSeverity.Success,
            NotificationSeverity.Warning => TimelineSeverity.Warning,
            NotificationSeverity.Error => TimelineSeverity.Error,
            _ => TimelineSeverity.Information
        }
    };

    private static string CreateDeduplicationKey(TimelineEvent item)
    {
        long minute = item.Timestamp.ToUniversalTime().Ticks / TimeSpan.TicksPerMinute;
        string title = item.Title.Equals("New Device", StringComparison.OrdinalIgnoreCase)
            ? "new-device"
            : item.Title.Trim().ToLowerInvariant();
        return $"{item.Category}|{title}|{minute}";
    }

    private static string FirstValue(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Unknown device";
}
