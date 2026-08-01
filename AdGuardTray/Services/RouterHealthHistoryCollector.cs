using AdGuardTray.Models;

namespace AdGuardTray.Services;

public sealed class RouterHealthHistoryCollector : IAsyncDisposable
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);
    private readonly HistoryRepository _repository;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RouterHealthMinuteSnapshot? _currentMinute;
    private double _cpuTotal;
    private double _memoryTotal;
    private int _cpuSampleCount;
    private int _memorySampleCount;
    private DateOnly? _lastPruneDateUtc;
    private Task? _disposeTask;
    private bool _disposalStarted;

    public RouterHealthHistoryCollector(HistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task RecordSnapshotAsync(
        DateTimeOffset timestampUtc,
        double? cpuUsagePercent,
        double? memoryUsagePercent,
        long? memoryUsedBytes,
        long? memoryTotalBytes,
        double? temperatureCelsius,
        double? storageUsagePercent,
        CancellationToken cancellationToken = default)
    {
        cpuUsagePercent = ValidPercent(cpuUsagePercent);
        memoryUsagePercent = ValidPercent(memoryUsagePercent);
        storageUsagePercent = ValidPercent(storageUsagePercent);
        if (_disposalStarted ||
            (cpuUsagePercent is null && memoryUsagePercent is null &&
             temperatureCelsius is null && storageUsagePercent is null))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposalStarted)
                return;

            DateTimeOffset minute = RoundDownToUtcMinute(timestampUtc);
            if (_currentMinute is not null &&
                _currentMinute.TimestampUtc != minute)
            {
                await PersistCurrentMinuteOffThreadAsync(cancellationToken)
                    .ConfigureAwait(false);
                ResetMinute();
            }

            _currentMinute ??= new RouterHealthMinuteSnapshot
            {
                TimestampUtc = minute
            };
            _currentMinute.SampleCount++;

            if (cpuUsagePercent.HasValue)
            {
                _cpuTotal += cpuUsagePercent.Value;
                _cpuSampleCount++;
                _currentMinute.AverageCpuUsagePercent =
                    _cpuTotal / _cpuSampleCount;
                _currentMinute.PeakCpuUsagePercent = Math.Max(
                    _currentMinute.PeakCpuUsagePercent ?? 0,
                    cpuUsagePercent.Value);
            }

            if (memoryUsagePercent.HasValue)
            {
                _memoryTotal += memoryUsagePercent.Value;
                _memorySampleCount++;
                _currentMinute.AverageMemoryUsagePercent =
                    _memoryTotal / _memorySampleCount;
                _currentMinute.PeakMemoryUsagePercent = Math.Max(
                    _currentMinute.PeakMemoryUsagePercent ?? 0,
                    memoryUsagePercent.Value);
            }

            if (memoryUsedBytes is >= 0)
                _currentMinute.MemoryUsedBytes = memoryUsedBytes;
            if (memoryTotalBytes is > 0)
                _currentMinute.MemoryTotalBytes = memoryTotalBytes;
            if (temperatureCelsius.HasValue)
                _currentMinute.TemperatureCelsius = temperatureCelsius;
            if (storageUsagePercent.HasValue)
                _currentMinute.StorageUsagePercent = storageUsagePercent;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PersistCurrentMinuteOffThreadAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null)
                return new ValueTask(_disposeTask);

            _disposalStarted = true;
            _disposeTask = FlushAsync(CancellationToken.None);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task PersistCurrentMinuteAsync(
        CancellationToken cancellationToken)
    {
        if (_currentMinute is null || _currentMinute.SampleCount == 0)
            return;

        await _repository.AddOrUpdateRouterHealthMinuteAsync(
                _currentMinute,
                cancellationToken)
            .ConfigureAwait(false);

        DateOnly todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_lastPruneDateUtc == todayUtc)
            return;

        await _repository.DeleteRouterHealthBeforeAsync(
                DateTimeOffset.UtcNow.Subtract(RetentionPeriod),
                cancellationToken)
            .ConfigureAwait(false);
        _lastPruneDateUtc = todayUtc;
    }

    private Task PersistCurrentMinuteOffThreadAsync(
        CancellationToken cancellationToken) =>
        Task.Run(
            () => PersistCurrentMinuteAsync(cancellationToken),
            cancellationToken);

    private void ResetMinute()
    {
        _currentMinute = null;
        _cpuTotal = 0;
        _memoryTotal = 0;
        _cpuSampleCount = 0;
        _memorySampleCount = 0;
    }

    private static double? ValidPercent(double? value) =>
        value is >= 0 and <= 100 ? value : null;

    private static DateTimeOffset RoundDownToUtcMinute(
        DateTimeOffset timestamp)
    {
        DateTimeOffset utc = timestamp.ToUniversalTime();
        return new DateTimeOffset(
            utc.Year,
            utc.Month,
            utc.Day,
            utc.Hour,
            utc.Minute,
            0,
            TimeSpan.Zero);
    }
}
