using System;

namespace RouterPilot.Models;

public enum DiagnosticExecutionOutcome
{
    Success,
    Error,
    Cancelled
}

public enum DiagnosticExecutionSource
{
    About,
    Maintenance
}

public sealed class DiagnosticHistoryEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public DiagnosticExecutionOutcome Outcome { get; init; }

    public DiagnosticExecutionSource Source { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? OutputPath { get; init; }

    public string DisplayText => $"[{Timestamp.ToLocalTime():HH:mm:ss}] Run Diagnostics — {Source}: {Message}";
}
