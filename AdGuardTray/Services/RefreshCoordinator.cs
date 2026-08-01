using System;
using System.Threading;
using System.Threading.Tasks;

namespace AdGuardTray.Services;

public sealed class RefreshCoordinator : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public bool IsRunning => _loopTask is { IsCompleted: false };

    public async Task<bool> RunOnceAsync(
        Func<CancellationToken, Task> refresh,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refresh);

        if (!await _gate.WaitAsync(0, cancellationToken)
                .ConfigureAwait(false))
            return false;

        try
        {
            await refresh(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Start(
        TimeSpan interval,
        Func<CancellationToken, Task> refresh)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));

        ArgumentNullException.ThrowIfNull(refresh);

        Stop();
        _loopCts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(
            interval,
            refresh,
            _loopCts.Token);
    }

    public void Stop()
    {
        _loopCts?.Cancel();
    }

    private async Task RunLoopAsync(
        TimeSpan interval,
        Func<CancellationToken, Task> refresh,
        CancellationToken cancellationToken)
    {
        await RunOnceAsync(refresh, cancellationToken)
            .ConfigureAwait(false);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(cancellationToken)
                   .ConfigureAwait(false))
        {
            await RunOnceAsync(refresh, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _loopCts?.Dispose();
        _gate.Dispose();
    }
}
