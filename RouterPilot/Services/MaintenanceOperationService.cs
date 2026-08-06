using System;
using System.Threading;
using System.Threading.Tasks;
using RouterPilot.Models;

namespace RouterPilot.Services;

public sealed class MaintenanceOperationService
{
    private readonly IRouterManagerProvider _routerManagerProvider;
    private readonly NotificationService _notificationService;
    private readonly MaintenanceHistoryService _historyService;
    private readonly DiagnosticsExecutionService _diagnosticsExecutionService;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    public MaintenanceOperationService(
        IRouterManagerProvider routerManagerProvider,
        NotificationService notificationService,
        MaintenanceHistoryService historyService,
        DiagnosticsExecutionService diagnosticsExecutionService)
    {
        _routerManagerProvider = routerManagerProvider;
        _notificationService = notificationService;
        _historyService = historyService;
        _diagnosticsExecutionService = diagnosticsExecutionService;
    }

    public async Task<MaintenanceOperationResult> ExecuteAsync(
        MaintenanceAction action,
        Func<Task> refreshAll,
        CancellationToken cancellationToken = default)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
        {
            return MaintenanceOperationResult.Cancelled(
                "Another maintenance action is already running.");
        }

        Guid executionId = Guid.NewGuid();
        try
        {
            string message = action switch
            {
                MaintenanceAction.RefreshAll => await RefreshAllAsync(refreshAll),
                MaintenanceAction.RestartWifi => await RestartWifiAsync(cancellationToken),
                MaintenanceAction.RestartAdGuard => await RestartAdGuardAsync(cancellationToken),
                MaintenanceAction.ReconnectWan => await ReconnectWanAsync(cancellationToken),
                MaintenanceAction.RebootRouter => await RebootRouterAsync(cancellationToken),
                MaintenanceAction.RunDiagnostics => await RunDiagnosticsAsync(cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };

            MaintenanceOperationResult result = MaintenanceOperationResult.Success(message);
            if (action != MaintenanceAction.RunDiagnostics)
            {
                await RecordAsync(action, result, executionId);
            }
            return result;
        }
        catch (MaintenanceOperationCancelledException)
        {
            return MaintenanceOperationResult.Cancelled("Diagnostics export cancelled.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MaintenanceOperationResult result = MaintenanceOperationResult.Cancelled("Maintenance action cancelled.");
            if (action != MaintenanceAction.RunDiagnostics)
            {
                await RecordAsync(action, result, executionId);
            }
            return result;
        }
        catch (Exception)
        {
            MaintenanceOperationResult result = MaintenanceOperationResult.Error(
                "RouterPilot could not complete this maintenance action.");
            if (action != MaintenanceAction.RunDiagnostics)
            {
                await RecordAsync(action, result, executionId);
            }
            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<string> RestartWifiAsync(CancellationToken cancellationToken)
    {
        RouterManager router = await _routerManagerProvider.GetRouterManagerAsync();
        string response = await router.RestartWifiAsync().WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        if (!response.Contains("successfully", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException();

        if ((await router.GetWifiRadiosAsync().WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)).Count == 0)
            throw new InvalidOperationException();

        return "Wi-Fi restarted and router interfaces are available.";
    }

    private async Task<string> RestartAdGuardAsync(CancellationToken cancellationToken)
    {
        RouterManager router = await _routerManagerProvider.GetRouterManagerAsync();
        await router.RestartAdGuardAsync().WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        if (!(await router.GetAdGuardStatusAsync().WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)).IsRunning)
            throw new InvalidOperationException();

        return "AdGuard Home restarted and is active.";
    }

    private async Task<string> ReconnectWanAsync(CancellationToken cancellationToken)
    {
        RouterManager router = await _routerManagerProvider.GetRouterManagerAsync();
        string response = await router.RestartWanAsync().WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        if (!response.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
            !(await router.GetNetworkInfoAsync().WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)).Connected)
        {
            throw new InvalidOperationException();
        }

        return "WAN reconnected and internet connectivity is available.";
    }

    private async Task<string> RebootRouterAsync(CancellationToken cancellationToken)
    {
        RouterManager router = await _routerManagerProvider.GetRouterManagerAsync();
        await router.RebootRouterAsync().WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        return "Reboot request accepted. RouterPilot will confirm recovery through normal refreshes.";
    }

    private async Task<string> RunDiagnosticsAsync(CancellationToken cancellationToken)
    {
        DiagnosticsExecutionResult result = await _diagnosticsExecutionService.RunAsync(
            DiagnosticExecutionSource.Maintenance,
            cancellationToken);
        return result.Outcome switch
        {
            DiagnosticExecutionOutcome.Success =>
                "Diagnostics completed. Open About for the detailed support report and export options.",
            DiagnosticExecutionOutcome.Cancelled => throw new MaintenanceOperationCancelledException(),
            _ => throw new InvalidOperationException(result.Message)
        };
    }

    private static async Task<string> RefreshAllAsync(Func<Task> refreshAll)
    {
        await refreshAll();
        return "RouterPilot refreshed all current dashboard data.";
    }

    private async Task RecordAsync(
        MaintenanceAction action,
        MaintenanceOperationResult result,
        Guid executionId)
    {
        await _historyService.AddAsync(new MaintenanceHistoryEntry
        {
            Id = executionId,
            Action = action,
            Outcome = result.Outcome,
            Message = result.Message
        });

        await _notificationService.AddAsync(new AppNotification
        {
            Title = result.Outcome == MaintenanceOutcome.Success
                ? "Maintenance action completed"
                : "Maintenance action failed",
            Message = MaintenanceActionPresentation.Title(action) + ": " + result.Message,
            Severity = result.Outcome == MaintenanceOutcome.Success
                ? NotificationSeverity.Success
                : result.Outcome == MaintenanceOutcome.Cancelled
                    ? NotificationSeverity.Warning
                    : NotificationSeverity.Error,
            Category = action == MaintenanceAction.RestartAdGuard
                ? NotificationCategory.AdGuard
                : NotificationCategory.Router,
            DeduplicationKey = "Maintenance-" + action + "-" + executionId
        });
    }
}

file sealed class MaintenanceOperationCancelledException : Exception;

public sealed record MaintenanceOperationResult(MaintenanceOutcome Outcome, string Message)
{
    public static MaintenanceOperationResult Success(string message) => new(MaintenanceOutcome.Success, message);
    public static MaintenanceOperationResult Error(string message) => new(MaintenanceOutcome.Error, message);
    public static MaintenanceOperationResult Cancelled(string message) => new(MaintenanceOutcome.Cancelled, message);
}
