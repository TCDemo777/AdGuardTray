using AdGuardTray.Models;

namespace AdGuardTray.Services.Intelligence;

public sealed class DeviceBehaviourRule : BehaviourRule
{
    public override Task<IEnumerable<BehaviourObservation>> EvaluateAsync(
        BehaviourAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var observations = new List<BehaviourObservation>();
        foreach (DeviceHistoryRecord device in analysis.Devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string mac = DeviceHistoryService.NormalizeMacAddress(device.MacAddress);
            DeviceConnectionEvent[] events = analysis.DeviceEvents
                .Where(item => DeviceHistoryService.NormalizeMacAddress(item.MacAddress) == mac)
                .OrderBy(item => item.TimestampUtc)
                .ToArray();
            int? onlineHour = MostCommonHour(events,
                DeviceConnectionEventType.Connected, DeviceConnectionEventType.FirstSeen);
            int? offlineHour = MostCommonHour(events, DeviceConnectionEventType.Disconnected);
            TimeSpan? averageSession = AverageSession(events);
            int daysSinceLastSeen = Math.Max(0,
                (int)(analysis.GeneratedAtUtc - device.LastSeen.ToUniversalTime()).TotalDays);
            string preferredNetwork = events
                .Where(item => !string.IsNullOrWhiteSpace(item.NetworkName))
                .GroupBy(item => item.NetworkName, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count()).Select(group => group.Key)
                .FirstOrDefault() ?? device.LastNetworkName;
            string commonIp = device.PreviousIpAddresses.Append(device.LastIpAddress)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count()).Select(group => group.Key)
                .FirstOrDefault() ?? string.Empty;
            double observedWeeks = Math.Max(1,
                (analysis.GeneratedAtUtc - device.FirstSeen.ToUniversalTime()).TotalDays / 7d);
            double reconnects = events.Count(item => item.EventType == DeviceConnectionEventType.Connected) /
                                observedWeeks;
            string name = First(device.FriendlyName, device.Hostname, device.MacAddress);
            analysis.DeviceProfiles[mac] = new DeviceBehaviourProfile
            {
                MacAddress = mac,
                DisplayName = name,
                TypicalOnlineHour = onlineHour,
                TypicalOfflineHour = offlineHour,
                AverageSessionDuration = averageSession,
                DaysSinceLastSeen = daysSinceLastSeen,
                ReconnectsPerWeek = reconnects,
                PreferredNetwork = preferredNetwork,
                MostCommonIpAddress = commonIp
            };

            if (daysSinceLastSeen >= 7)
                observations.Add(new BehaviourObservation
                {
                    Title = "Device not recently seen",
                    Description = $"{name} has not been seen for {daysSinceLastSeen} days.",
                    Category = "Device",
                    Severity = daysSinceLastSeen >= 30
                        ? BehaviourObservationSeverity.Warning
                        : BehaviourObservationSeverity.Information,
                    Priority = Math.Min(90, 45 + daysSinceLastSeen)
                });
            else if (onlineHour is not null && events.Length >= 3)
                observations.Add(new BehaviourObservation
                {
                    Title = "Typical device schedule",
                    Description = $"{name} normally appears between {onlineHour:00}:00 and {(onlineHour + 1) % 24:00}:00.",
                    Category = "Device",
                    Severity = BehaviourObservationSeverity.Information,
                    Priority = 25
                });
        }
        return Task.FromResult<IEnumerable<BehaviourObservation>>(observations);
    }

    private static int? MostCommonHour(DeviceConnectionEvent[] events,
        params DeviceConnectionEventType[] types) => events
        .Where(item => types.Contains(item.EventType))
        .GroupBy(item => item.TimestampUtc.ToLocalTime().Hour)
        .OrderByDescending(group => group.Count()).Select(group => (int?)group.Key)
        .FirstOrDefault();

    private static TimeSpan? AverageSession(DeviceConnectionEvent[] events)
    {
        var durations = new List<TimeSpan>();
        DateTimeOffset? connected = null;
        foreach (DeviceConnectionEvent item in events)
        {
            if (item.EventType is DeviceConnectionEventType.Connected or DeviceConnectionEventType.FirstSeen)
                connected = item.TimestampUtc;
            else if (item.EventType == DeviceConnectionEventType.Disconnected && connected is { } start && item.TimestampUtc > start)
            {
                durations.Add(item.TimestampUtc - start);
                connected = null;
            }
        }
        return durations.Count == 0 ? null
            : TimeSpan.FromTicks((long)durations.Average(value => value.Ticks));
    }

    private static string First(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Unknown device";
}
