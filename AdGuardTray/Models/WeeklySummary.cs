namespace AdGuardTray.Models;

public sealed class WeeklySummary
{
    public DateTimeOffset PeriodStartUtc { get; init; }
    public DateTimeOffset PeriodEndUtc { get; init; }
    public long? TotalWanDownloadBytes { get; init; }
    public long? TotalWanUploadBytes { get; init; }
    public double? AverageDownloadMbps { get; init; }
    public double? PeakDownloadMbps { get; init; }
    public double? AverageUploadMbps { get; init; }
    public double? PeakUploadMbps { get; init; }
    public double? AverageCpuPercent { get; init; }
    public double? PeakCpuPercent { get; init; }
    public double? AverageMemoryPercent { get; init; }
    public double? PeakMemoryPercent { get; init; }
    public int NewDevicesCount { get; init; }
    public int ConnectionEventsCount { get; init; }
    public int RouterOfflineEventsCount { get; init; }
    public int AdGuardDisabledEventsCount { get; init; }
    public string? MostActiveNetworkName { get; init; }
    public IReadOnlyList<WeeklySummaryHighlight> Highlights { get; init; } =
        Array.Empty<WeeklySummaryHighlight>();
    public int HistoricalDataPointCount { get; init; }

    public bool HasEnoughData => HistoricalDataPointCount >= 2;

    public string PeriodDisplay =>
        $"{PeriodStartUtc.ToLocalTime():dd MMM} – {PeriodEndUtc.ToLocalTime():dd MMM yyyy}";

    public string AverageDownloadDisplay =>
        AverageDownloadMbps.HasValue ? $"{AverageDownloadMbps:0.00} Mbps" : "—";

    public string PeakDownloadDisplay =>
        PeakDownloadMbps.HasValue ? $"{PeakDownloadMbps:0.00} Mbps" : "—";

    public string AverageCpuDisplay =>
        AverageCpuPercent.HasValue ? $"{AverageCpuPercent:0.0}%" : "—";

    public string PeakMemoryDisplay =>
        PeakMemoryPercent.HasValue ? $"{PeakMemoryPercent:0.0}%" : "—";
}

public sealed class WeeklySummaryHighlight
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public InsightSeverity Severity { get; init; }
    public InsightCategory Category { get; init; }
}

public sealed class WanHistoryAggregate
{
    public int DataPointCount { get; init; }
    public double? AverageDownloadMbps { get; init; }
    public double? PeakDownloadMbps { get; init; }
    public double? AverageUploadMbps { get; init; }
    public double? PeakUploadMbps { get; init; }
    public long? TotalDownloadBytes { get; init; }
    public long? TotalUploadBytes { get; init; }
}

public sealed class RouterHealthAggregate
{
    public int DataPointCount { get; init; }
    public double? AverageCpuPercent { get; init; }
    public double? PeakCpuPercent { get; init; }
    public double? AverageMemoryPercent { get; init; }
    public double? PeakMemoryPercent { get; init; }
}

public sealed class DeviceConnectionAggregate
{
    public int EventCount { get; init; }
    public string? MostActiveNetworkName { get; init; }
}
