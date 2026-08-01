using System;

namespace AdGuardTray.Models;

public enum InsightSeverity
{
    Information,
    Warning,
    Critical
}

public enum InsightCategory
{
    System,
    Router,
    Internet,
    AdGuard,
    Device
}

public enum InsightActionKind
{
    RebootRouter,
    EnableProtection,
    ViewNotifications,
    OpenClients,
    ViewAnalytics
}

public sealed class Insight
{
    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public InsightSeverity Severity { get; init; }

    public InsightCategory Category { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string? ActionLabel { get; init; }

    public InsightActionKind? Action { get; init; }

    public bool CanExecuteAction =>
        Action.HasValue && !string.IsNullOrWhiteSpace(ActionLabel);
}
