namespace AdGuardTray.Models;

public sealed class WanMinuteSnapshot
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public double AverageDownloadMbps { get; set; }
    public double AverageUploadMbps { get; set; }
    public double PeakDownloadMbps { get; set; }
    public double PeakUploadMbps { get; set; }
    public long ReceivedBytesTotal { get; set; }
    public long TransmittedBytesTotal { get; set; }
    public int SampleCount { get; set; }
}

public sealed class WanHistoryChartPoint
{
    public DateTimeOffset TimestampUtc { get; init; }
    public double AverageMbps { get; init; }
    public double PeakMbps { get; init; }
}
