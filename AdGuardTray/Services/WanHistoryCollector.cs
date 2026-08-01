using AdGuardTray.Models;

namespace AdGuardTray.Services;

public sealed class WanHistoryCollector : IAsyncDisposable
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);
    private readonly HistoryRepository _repository;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WanMinuteSnapshot? _currentMinute;
    private double _downloadTotal;
    private double _uploadTotal;
    private DateOnly? _lastPruneDateUtc;
    private Task? _disposeTask;
    private bool _disposalStarted;

    public WanHistoryCollector(HistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task RecordSampleAsync(
        DateTimeOffset timestampUtc,
        double downloadMbps,
        double uploadMbps,
        long receivedBytesTotal,
        long transmittedBytesTotal,
        CancellationToken cancellationToken = default)
    {
        if (_disposalStarted ||
            downloadMbps < 0 || uploadMbps < 0)
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
                _currentMinute = null;
                _downloadTotal = 0;
                _uploadTotal = 0;
            }

            _currentMinute ??= new WanMinuteSnapshot
            {
                TimestampUtc = minute
            };

            _downloadTotal += downloadMbps;
            _uploadTotal += uploadMbps;
            _currentMinute.SampleCount++;
            _currentMinute.AverageDownloadMbps =
                _downloadTotal / _currentMinute.SampleCount;
            _currentMinute.AverageUploadMbps =
                _uploadTotal / _currentMinute.SampleCount;
            _currentMinute.PeakDownloadMbps = Math.Max(
                _currentMinute.PeakDownloadMbps,
                downloadMbps);
            _currentMinute.PeakUploadMbps = Math.Max(
                _currentMinute.PeakUploadMbps,
                uploadMbps);
            _currentMinute.ReceivedBytesTotal = receivedBytesTotal;
            _currentMinute.TransmittedBytesTotal = transmittedBytesTotal;
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

        await _repository.AddOrUpdateWanMinuteAsync(
                _currentMinute,
                cancellationToken)
            .ConfigureAwait(false);

        DateOnly todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_lastPruneDateUtc == todayUtc)
            return;

        _lastPruneDateUtc = todayUtc;
        await _repository.DeleteWanHistoryBeforeAsync(
                DateTimeOffset.UtcNow.Subtract(RetentionPeriod),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task PersistCurrentMinuteOffThreadAsync(
        CancellationToken cancellationToken) =>
        Task.Run(
            () => PersistCurrentMinuteAsync(cancellationToken),
            cancellationToken);

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
