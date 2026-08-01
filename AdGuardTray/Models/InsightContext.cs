using System;
using System.Collections.Generic;

namespace AdGuardTray.Models;

public sealed class InsightContext
{
    public required DateTimeOffset EvaluatedAt { get; init; }
    public required bool RouterConnected { get; init; }
    public required RouterInfo RouterHealth { get; init; }
    public required double CpuPercentage { get; init; }
    public required double MemoryPercentage { get; init; }
    public required double StoragePercentage { get; init; }
    public required NetworkInfo WanStatus { get; init; }
    public required AdGuardStatus AdGuardStatus { get; init; }
    public required bool AdGuardProtectionStatusKnown { get; init; }
    public required bool AdGuardProtectionEnabled { get; init; }
    public required AdGuardStatistics DnsStatistics { get; init; }
    public required IReadOnlyCollection<DeviceHistoryRecord> DeviceHistory { get; init; }
    public required IReadOnlyCollection<AppNotification> NotificationHistory { get; init; }
    public required IReadOnlyCollection<string> ConnectedClientMacAddresses { get; init; }
    public required bool ConnectedClientSnapshotComplete { get; init; }
}
