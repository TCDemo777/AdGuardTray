namespace AdGuardTray.Models;

public enum DeviceConnectionEventType
{
    Connected,
    Disconnected,
    IpChanged,
    NetworkChanged,
    FirstSeen
}

public sealed class DeviceConnectionEvent
{
    public long Id { get; set; }

    public string MacAddress { get; set; } = string.Empty;

    public DateTimeOffset TimestampUtc { get; set; }

    public DeviceConnectionEventType EventType { get; set; }

    public string IpAddress { get; set; } = string.Empty;

    public string NetworkName { get; set; } = string.Empty;

    public string Hostname { get; set; } = string.Empty;

    public string FriendlyName { get; set; } = string.Empty;

    public string TimestampDisplay
    {
        get
        {
            DateTime local = TimestampUtc.ToLocalTime().DateTime;
            DateTime today = DateTime.Today;
            string day = local.Date == today
                ? "Today"
                : local.Date == today.AddDays(-1)
                    ? "Yesterday"
                    : local.ToString("dd MMM yyyy");
            return $"{day} {local:HH:mm}";
        }
    }

    public string EventTypeDisplay => EventType switch
    {
        DeviceConnectionEventType.IpChanged => "IP changed",
        DeviceConnectionEventType.NetworkChanged => "Network changed",
        DeviceConnectionEventType.FirstSeen => "First seen",
        _ => EventType.ToString()
    };

    public string Details => EventType switch
    {
        DeviceConnectionEventType.IpChanged => IpAddress,
        DeviceConnectionEventType.NetworkChanged => NetworkName,
        _ => string.Empty
    };

    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
}
