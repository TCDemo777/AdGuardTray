namespace AdGuardTray.Models;

public sealed class RouterHealthMinuteSnapshot
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public double? AverageCpuUsagePercent { get; set; }
    public double? PeakCpuUsagePercent { get; set; }
    public double? AverageMemoryUsagePercent { get; set; }
    public double? PeakMemoryUsagePercent { get; set; }
    public long? MemoryUsedBytes { get; set; }
    public long? MemoryTotalBytes { get; set; }
    public double? TemperatureCelsius { get; set; }
    public double? StorageUsagePercent { get; set; }
    public int SampleCount { get; set; }
}

public sealed class RouterHealthChartPoint
{
    public DateTimeOffset TimestampUtc { get; init; }
    public double AveragePercent { get; init; }
    public double PeakPercent { get; init; }
}
