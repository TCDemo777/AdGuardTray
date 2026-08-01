namespace AdGuardTray.Models;

public sealed class DiagnosticExportOptions
{
    public required string DestinationPath { get; init; }
    public bool IncludeDeviceIdentifiers { get; init; }
    public DiagnosticRuntimeState RuntimeState { get; init; } = new();
    public string SupportLog { get; init; } = string.Empty;
}

public sealed class DiagnosticRuntimeState
{
    public bool RouterOnline { get; init; }
    public bool InternetOnline { get; init; }
    public string RouterModel { get; init; } = string.Empty;
    public string FirmwareVersion { get; init; } = string.Empty;
    public bool? AdGuardProtectionEnabled { get; init; }
    public double? CpuPercent { get; init; }
    public double? MemoryPercent { get; init; }
    public double? StoragePercent { get; init; }
    public string Temperature { get; init; } = string.Empty;
    public string DownloadRate { get; init; } = string.Empty;
    public string UploadRate { get; init; } = string.Empty;
    public int ConnectedClientCount { get; init; }
    public int NetworkCount { get; init; }
    public int NotificationUnreadCount { get; init; }
    public IReadOnlyList<RefreshTaskDiagnosticState> RefreshTasks { get; init; } =
        Array.Empty<RefreshTaskDiagnosticState>();
}

public sealed record RefreshTaskDiagnosticState(
    string Name,
    TimeSpan Interval,
    bool Enabled,
    bool Running);

public sealed record DiagnosticExportProgress(int Percentage, string Status);

public sealed record DatabaseTableHealth(
    string Name,
    long RowCount,
    string? OldestTimestampUtc,
    string? NewestTimestampUtc);

public sealed class DatabaseHealthReport
{
    public required string DatabasePath { get; init; }
    public long FileSizeBytes { get; init; }
    public int SchemaVersion { get; init; }
    public string IntegrityCheck { get; init; } = "Unavailable";
    public IReadOnlyList<DatabaseTableHealth> Tables { get; init; } =
        Array.Empty<DatabaseTableHealth>();
}
