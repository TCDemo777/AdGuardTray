using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using AdGuardTray.Models;

namespace AdGuardTray.Services;

public sealed class DiagnosticExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _exportGate = new(1, 1);
    private readonly SettingsService _settingsService;
    private readonly NotificationService _notificationService;
    private readonly DeviceHistoryService _deviceHistoryService;
    private readonly HistoryRepository _historyRepository;
    private readonly IDataStore _dataStore;
    private readonly DiagnosticRedactor _redactor;
    private readonly Dispatcher _dispatcher;

    public DiagnosticExportService(
        SettingsService settingsService,
        NotificationService notificationService,
        DeviceHistoryService deviceHistoryService,
        HistoryRepository historyRepository,
        IDataStore dataStore,
        DiagnosticRedactor redactor,
        Dispatcher dispatcher)
    {
        _settingsService = settingsService;
        _notificationService = notificationService;
        _deviceHistoryService = deviceHistoryService;
        _historyRepository = historyRepository;
        _dataStore = dataStore;
        _redactor = redactor;
        _dispatcher = dispatcher;
    }

    public async Task ExportAsync(
        DiagnosticExportOptions options,
        IProgress<DiagnosticExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        await _exportGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        string stagingFolder = Path.Combine(
            Path.GetTempPath(), "RouterPilot-Diagnostics-" + Guid.NewGuid().ToString("N"));
        string destination = Path.GetFullPath(options.DestinationPath);
        string partialZip = destination + ".partial-" + Guid.NewGuid().ToString("N");

        try
        {
            Directory.CreateDirectory(stagingFolder);
            progress?.Report(new(5, "Collecting application details..."));
            cancellationToken.ThrowIfCancellationRequested();

            AppSettings settings = _settingsService.Load();
            AppNotification[] notifications = await _dispatcher.InvokeAsync(() =>
                _notificationService.Notifications.Take(100).ToArray());
            IReadOnlyCollection<DeviceHistoryRecord> devices =
                _deviceHistoryService.Records;
            string RedactContent(string? text) => options.IncludeDeviceIdentifiers
                ? _redactor.RedactText(text)
                : _redactor.RedactDeviceIdentifiers(text, devices);

            await WriteTextAsync(stagingFolder, "summary.txt",
                BuildSummary(settings, options.RuntimeState), cancellationToken);
            await WriteJsonAsync(stagingFolder, "settings-redacted.json",
                _redactor.RedactObject(settings), cancellationToken);
            await WriteJsonAsync(stagingFolder, "notifications-recent.json",
                notifications.Select(item => new
                {
                    item.Timestamp,
                    Title = RedactContent(item.Title),
                    Message = RedactContent(item.Message),
                    Severity = item.Severity.ToString(),
                    Category = item.Category.ToString(),
                    item.IsRead
                }), cancellationToken);

            progress?.Report(new(35, "Checking history database..."));
            DatabaseHealthReport databaseHealth = await _historyRepository
                .GetDatabaseHealthAsync(cancellationToken).ConfigureAwait(false);
            await WriteTextAsync(stagingFolder, "database-health.txt",
                BuildDatabaseHealth(databaseHealth), cancellationToken);

            await WriteJsonAsync(stagingFolder, "runtime-state.json",
                BuildRuntimeState(options.RuntimeState), cancellationToken);

            if (options.IncludeDeviceIdentifiers)
            {
                await WriteJsonAsync(stagingFolder, "device-identifiers.json",
                    _deviceHistoryService.Records.Select(record => new
                    {
                        record.MacAddress,
                        record.LastIpAddress,
                        record.Hostname,
                        record.FriendlyName,
                        record.LastSsid,
                        record.LastNetworkName
                    }), cancellationToken);
            }

            progress?.Report(new(60, "Collecting recent logs..."));
            await AddLogsAsync(stagingFolder, options.SupportLog, devices,
                    options.IncludeDeviceIdentifiers, cancellationToken)
                .ConfigureAwait(false);
            await WriteTextAsync(stagingFolder, "README.txt", BuildReadme(), cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(80, "Creating support bundle..."));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await Task.Run(() => ZipFile.CreateFromDirectory(
                stagingFolder, partialZip, CompressionLevel.Optimal, false), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partialZip, destination, overwrite: true);
            progress?.Report(new(100, "Diagnostics exported successfully."));
        }
        finally
        {
            TryDeleteFile(partialZip);
            TryDeleteDirectory(stagingFolder);
            _exportGate.Release();
        }
    }

    private string BuildSummary(AppSettings settings, DiagnosticRuntimeState state)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";
        string buildDate = File.Exists(assembly.Location)
            ? File.GetLastWriteTimeUtc(assembly.Location).ToString("O")
            : "unknown";
        string? embeddedSource = informational.Contains('+')
            ? informational[(informational.IndexOf('+') + 1)..]
            : null;
        var lines = new List<string>
        {
            "RouterPilot diagnostic summary",
            "==============================",
            $"Version: {assembly.GetName().Version}",
            $"Informational version: {informational}",
            $"Build date (assembly timestamp UTC): {buildDate}",
            $"Windows: {RuntimeInformation.OSDescription}",
            $".NET runtime: {RuntimeInformation.FrameworkDescription}",
            $"Process architecture: {RuntimeInformation.ProcessArchitecture}",
            $"Application data path: {Path.GetDirectoryName(_dataStore.DatabasePath)}",
            $"Generated UTC: {DateTimeOffset.UtcNow:O}",
            $"Generated local: {DateTimeOffset.Now:O}"
        };
        if (!string.IsNullOrWhiteSpace(embeddedSource))
            lines.Add($"Embedded source revision: {embeddedSource}");
        lines.AddRange(new[]
        {
            "", "Configuration", "-------------",
            $"Router host: {_redactor.RedactText(settings.RouterHost)}",
            $"Router credentials: {DiagnosticRedactor.RedactedValue}",
            $"Router scheme/port: {(settings.UseRouterHttps ? "https" : "http")}:{settings.RouterPort}",
            $"AdGuard scheme/port: {(settings.UseAdGuardHttps ? "https" : "http")}:{settings.AdGuardPort}",
            $"Theme: {settings.Theme}",
            $"Configured dashboard refresh: {settings.RefreshIntervalSeconds} seconds",
            "", "Current state", "-------------",
            $"Router model: {ValueOrUnavailable(state.RouterModel)}",
            $"Firmware: {ValueOrUnavailable(state.FirmwareVersion)}",
            $"Router connectivity: {(state.RouterOnline ? "Online" : "Offline")}",
            $"AdGuard protection: {FormatNullableState(state.AdGuardProtectionEnabled)}"
        });
        foreach (RefreshTaskDiagnosticState task in state.RefreshTasks)
            lines.Add($"Refresh {task.Name}: {task.Interval.TotalSeconds:0.##}s; enabled={task.Enabled}; running={task.Running}");
        return string.Join(Environment.NewLine, lines);
    }

    private static object BuildRuntimeState(DiagnosticRuntimeState state) => new
    {
        state.RouterOnline,
        state.InternetOnline,
        CpuPercent = state.CpuPercent,
        MemoryPercent = state.MemoryPercent,
        StoragePercent = state.StoragePercent,
        Temperature = ValueOrUnavailable(state.Temperature),
        DownloadRate = state.DownloadRate,
        UploadRate = state.UploadRate,
        state.ConnectedClientCount,
        state.NetworkCount,
        state.NotificationUnreadCount,
        HistoryDatabaseAvailable = true,
        RefreshTasks = state.RefreshTasks.Select(task => new
        {
            task.Name,
            IntervalSeconds = task.Interval.TotalSeconds,
            task.Enabled,
            task.Running
        })
    };

    private static string BuildDatabaseHealth(DatabaseHealthReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Database path: {report.DatabasePath}");
        builder.AppendLine($"File size: {report.FileSizeBytes} bytes");
        builder.AppendLine($"Schema version: {report.SchemaVersion}");
        builder.AppendLine($"Integrity check: {report.IntegrityCheck}");
        builder.AppendLine();
        builder.AppendLine("Tables (aggregate metadata only)");
        foreach (DatabaseTableHealth table in report.Tables)
        {
            builder.AppendLine($"- {table.Name}: {table.RowCount} rows");
            if (table.OldestTimestampUtc is not null || table.NewestTimestampUtc is not null)
                builder.AppendLine($"  oldest={table.OldestTimestampUtc ?? "n/a"}; newest={table.NewestTimestampUtc ?? "n/a"}");
        }
        return builder.ToString();
    }

    private async Task AddLogsAsync(
        string stagingFolder,
        string supportLog,
        IReadOnlyCollection<DeviceHistoryRecord> devices,
        bool includeDeviceIdentifiers,
        CancellationToken cancellationToken)
    {
        string logsFolder = Path.Combine(stagingFolder, "logs");
        Directory.CreateDirectory(logsFolder);
        int included = 0;
        string dataFolder = Path.GetDirectoryName(_dataStore.DatabasePath)!;
        if (Directory.Exists(dataFolder))
        {
            IEnumerable<FileInfo> logs = new DirectoryInfo(dataFolder)
                .EnumerateFiles("*.log", SearchOption.AllDirectories)
                .Where(file => file.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-7))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(2);
            foreach (FileInfo log in logs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text = await File.ReadAllTextAsync(log.FullName, cancellationToken)
                    .ConfigureAwait(false);
                await File.WriteAllTextAsync(
                    Path.Combine(logsFolder, log.Name),
                    includeDeviceIdentifiers
                        ? _redactor.RedactText(text)
                        : _redactor.RedactDeviceIdentifiers(text, devices),
                    Encoding.UTF8, cancellationToken)
                    .ConfigureAwait(false);
                included++;
            }
        }
        if (!string.IsNullOrWhiteSpace(supportLog))
        {
            await File.WriteAllTextAsync(Path.Combine(logsFolder, "support-session.log"),
                includeDeviceIdentifiers
                    ? _redactor.RedactText(supportLog)
                    : _redactor.RedactDeviceIdentifiers(supportLog, devices),
                Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
            included++;
        }
        if (included == 0)
            await File.WriteAllTextAsync(Path.Combine(logsFolder, "logs-not-found.txt"),
                "No recent RouterPilot log files were found.", Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
    }

    private static string BuildReadme() => """
        RouterPilot diagnostic support bundle
        =====================================

        This bundle contains application/build details, redacted settings, recent
        notification metadata, aggregate SQLite health information, runtime state,
        and recent redacted logs when available.

        Passwords, tokens, credentials, raw databases, DNS query contents, full client
        exports, and screenshots are intentionally excluded. Device identifiers are
        included only in device-identifiers.json when you explicitly select that option.

        Automated redaction reduces risk but cannot anticipate every value contained in
        free-form errors. Please review every file before sharing this bundle publicly.
        """;

    private static Task WriteTextAsync(string folder, string name, string content,
        CancellationToken token) => File.WriteAllTextAsync(
            Path.Combine(folder, name), content, Encoding.UTF8, token);

    private static Task WriteJsonAsync(string folder, string name, object value,
        CancellationToken token) => File.WriteAllTextAsync(
            Path.Combine(folder, name), JsonSerializer.Serialize(value, JsonOptions),
            Encoding.UTF8, token);

    private static string ValueOrUnavailable(string? value) =>
        string.IsNullOrWhiteSpace(value) || value == "-" ? "Unavailable" : value;

    private static string FormatNullableState(bool? value) => value switch
    {
        true => "Enabled",
        false => "Disabled",
        null => "Unavailable"
    };

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}
