using System;

namespace RouterPilot.Models;

public enum MaintenanceAction
{
    RestartWifi,
    RestartAdGuard,
    RebootRouter,
    ReconnectWan,
    RefreshAll,
    RunDiagnostics
}

public enum MaintenanceOutcome
{
    Success,
    Error,
    Cancelled
}

public sealed class MaintenanceHistoryEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public MaintenanceAction Action { get; init; }

    public string? Source { get; init; }
    public MaintenanceOutcome Outcome { get; init; }
    public string Message { get; init; } = string.Empty;

    public string TimestampDisplay => Timestamp.ToLocalTime().ToString("dd MMM yyyy HH:mm");
    public string ActionDisplay => string.IsNullOrWhiteSpace(Source)
        ? MaintenanceActionPresentation.Title(Action)
        : $"{MaintenanceActionPresentation.Title(Action)} — {Source}";
    public string OutcomeDisplay => Outcome switch
    {
        MaintenanceOutcome.Success => "Success",
        MaintenanceOutcome.Error => "Error",
        _ => "Cancelled"
    };
}

public static class MaintenanceActionPresentation
{
    public static string Title(MaintenanceAction action) => action switch
    {
        MaintenanceAction.RestartWifi => "Restart Wi-Fi",
        MaintenanceAction.RestartAdGuard => "Restart AdGuard Home",
        MaintenanceAction.RebootRouter => "Reboot Router",
        MaintenanceAction.ReconnectWan => "Reconnect WAN",
        MaintenanceAction.RefreshAll => "Refresh All",
        MaintenanceAction.RunDiagnostics => "Run Diagnostics",
        _ => action.ToString()
    };
}
