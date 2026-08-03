using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AdGuardTray.Services;

internal sealed class SingleInstanceCoordinator : IAsyncDisposable, IDisposable
{
    internal const string MutexName = @"Local\RouterPilot.SingleInstance";
    internal const string ActivationEventName = @"Local\RouterPilot.ActivateExisting";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activationEvent;
    private readonly CancellationTokenSource _listenerCancellation = new();
    private readonly Task? _listenerTask;
    private int _disposeStarted;
    private bool _ownsMutex;

    private SingleInstanceCoordinator(
        Mutex mutex,
        EventWaitHandle? activationEvent,
        Action activationRequested)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        _ownsMutex = true;

        if (_activationEvent is not null)
        {
            _listenerTask = Task.Run(
                () => ListenForActivation(activationRequested));
        }
    }

    public static bool TryAcquire(
        Action activationRequested,
        out SingleInstanceCoordinator? coordinator)
    {
        coordinator = null;
        EventWaitHandle? activationEvent = null;
        Mutex? mutex = null;

        try
        {
            // Create the activation object first so a process that loses the
            // mutex can never signal before the primary has an open handle.
            try
            {
                activationEvent = new EventWaitHandle(
                    false,
                    EventResetMode.AutoReset,
                    ActivationEventName);
            }
            catch (Exception)
            {
                Debug.WriteLine(
                    "RouterPilot single-instance activation IPC is unavailable.");
            }

            mutex = new Mutex(false, MutexName);
            bool ownsMutex;

            try
            {
                ownsMutex = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                try
                {
                    activationEvent?.Set();
                }
                catch (Exception ex) when (
                    ex is ObjectDisposedException or UnauthorizedAccessException)
                {
                    Debug.WriteLine(
                        "RouterPilot could not signal the existing instance.");
                }

                activationEvent?.Dispose();
                mutex.Dispose();
                return false;
            }

            coordinator = new SingleInstanceCoordinator(
                mutex,
                activationEvent,
                activationRequested);
            return true;
        }
        catch
        {
            activationEvent?.Dispose();
            mutex?.Dispose();
            throw;
        }
    }

    private void ListenForActivation(Action activationRequested)
    {
        if (_activationEvent is null)
            return;

        WaitHandle[] handles =
        {
            _activationEvent,
            _listenerCancellation.Token.WaitHandle
        };

        try
        {
            while (!_listenerCancellation.IsCancellationRequested)
            {
                int signalled = WaitHandle.WaitAny(handles);
                if (signalled != 0 || _listenerCancellation.IsCancellationRequested)
                    break;

                try
                {
                    activationRequested();
                }
                catch (Exception)
                {
                    Debug.WriteLine(
                        "RouterPilot could not process an activation request.");
                }
            }
        }
        catch (ObjectDisposedException) when (
            _listenerCancellation.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        _listenerCancellation.Cancel();

        try
        {
            _activationEvent?.Set();
        }
        catch (ObjectDisposedException)
        {
        }

        if (_listenerTask is not null)
        {
            try
            {
                // Disposal is initiated on the WPF dispatcher thread, which
                // also owns the mutex and therefore must release it.
                await _listenerTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        ReleaseHandles();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        _listenerCancellation.Cancel();

        try
        {
            _activationEvent?.Set();
            _listenerTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }

        ReleaseHandles();
    }

    private void ReleaseHandles()
    {
        _activationEvent?.Dispose();
        _listenerCancellation.Dispose();

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _ownsMutex = false;
        }

        _mutex.Dispose();
    }
}
