using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AdGuardTray.Services;

public sealed class RefreshCoordinator : IAsyncDisposable
{
    private sealed class RefreshTaskRegistration
    {
        public required string Name { get; init; }

        public required TimeSpan Interval { get; set; }

        public required Func<CancellationToken, Task> Callback { get; init; }

        public bool Enabled { get; set; }

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public CancellationTokenSource? LoopCancellation { get; set; }

        public Task? LoopTask { get; set; }
    }

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, RefreshTaskRegistration> _tasks =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public void Register(
        string name,
        TimeSpan interval,
        Func<CancellationToken, Task> callback,
        bool enabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        lock (_syncRoot)
        {
            ThrowIfDisposed();

            if (_tasks.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"A refresh task named '{name}' is already registered.");
            }

            var registration = new RefreshTaskRegistration
            {
                Name = name,
                Interval = interval,
                Callback = callback,
                Enabled = enabled
            };

            _tasks.Add(name, registration);

            if (enabled)
            {
                StartLoop(registration);
            }
        }
    }

    public void SetEnabled(string name, bool enabled)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            RefreshTaskRegistration registration = GetTask(name);

            if (registration.Enabled == enabled)
            {
                return;
            }

            registration.Enabled = enabled;

            if (enabled)
            {
                StartLoop(registration);
            }
            else
            {
                StopLoop(registration);
            }
        }
    }

    public void UpdateInterval(string name, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            RefreshTaskRegistration registration = GetTask(name);

            if (registration.Interval == interval)
            {
                return;
            }

            registration.Interval = interval;

            if (registration.Enabled)
            {
                StopLoop(registration);
                StartLoop(registration);
            }
        }
    }

    public Task<bool> RunNowAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        RefreshTaskRegistration registration;

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            registration = GetTask(name);
        }

        return ExecuteAsync(registration, cancellationToken);
    }

    public void StopAll()
    {
        lock (_syncRoot)
        {
            foreach (RefreshTaskRegistration registration in _tasks.Values)
            {
                registration.Enabled = false;
                StopLoop(registration);
            }
        }
    }

    private RefreshTaskRegistration GetTask(string name)
    {
        if (!_tasks.TryGetValue(name, out RefreshTaskRegistration? task))
        {
            throw new KeyNotFoundException(
                $"No refresh task named '{name}' is registered.");
        }

        return task;
    }

    private void StartLoop(RefreshTaskRegistration registration)
    {
        var cancellation = new CancellationTokenSource();
        registration.LoopCancellation = cancellation;
        registration.LoopTask = RunLoopAsync(
            registration,
            cancellation.Token);
    }

    private static void StopLoop(RefreshTaskRegistration registration)
    {
        registration.LoopCancellation?.Cancel();
    }

    private static async Task RunLoopAsync(
        RefreshTaskRegistration registration,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(registration.Interval);

            while (await timer.WaitForNextTickAsync(cancellationToken)
                       .ConfigureAwait(false))
            {
                try
                {
                    await ExecuteAsync(registration, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"Refresh task '{registration.Name}' failed: {ex}");
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task<bool> ExecuteAsync(
        RefreshTaskRegistration registration,
        CancellationToken cancellationToken)
    {
        if (!await registration.Gate
                .WaitAsync(0, cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            await registration.Callback(cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        finally
        {
            registration.Gate.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        Task[] runningTasks;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (RefreshTaskRegistration registration in _tasks.Values)
            {
                registration.Enabled = false;
                StopLoop(registration);
            }

            runningTasks = _tasks.Values
                .Select(registration => registration.LoopTask)
                .OfType<Task>()
                .ToArray();
        }

        try
        {
            await Task.WhenAll(runningTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        lock (_syncRoot)
        {
            foreach (RefreshTaskRegistration registration in _tasks.Values)
            {
                registration.LoopCancellation?.Dispose();
                registration.Gate.Dispose();
            }

            _tasks.Clear();
        }
    }
}
