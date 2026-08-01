using AdGuardTray.Models;
using System.Windows.Threading;

namespace AdGuardTray.Services;

public sealed class WeeklySummaryService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private readonly HistoryRepository _historyRepository;
    private readonly DeviceHistoryService _deviceHistoryService;
    private readonly NotificationService _notificationService;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private WeeklySummary? _cachedSummary;
    private DateTimeOffset _cachedAtUtc;

    public WeeklySummaryService(
        HistoryRepository historyRepository,
        DeviceHistoryService deviceHistoryService,
        NotificationService notificationService,
        Dispatcher dispatcher)
    {
        _historyRepository = historyRepository;
        _deviceHistoryService = deviceHistoryService;
        _notificationService = notificationService;
        _dispatcher = dispatcher;
    }

    public async Task<WeeklySummary> GetSummaryAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        if (!forceRefresh &&
            _cachedSummary is not null &&
            nowUtc - _cachedAtUtc < CacheDuration)
        {
            return _cachedSummary;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            nowUtc = DateTimeOffset.UtcNow;
            if (!forceRefresh &&
                _cachedSummary is not null &&
                nowUtc - _cachedAtUtc < CacheDuration)
            {
                return _cachedSummary;
            }

            DateTimeOffset startUtc = nowUtc.Subtract(TimeSpan.FromDays(7));
            AppNotification[] notifications = await _dispatcher.InvokeAsync(
                () => _notificationService.Notifications.ToArray());
            DeviceHistoryRecord[] devices =
                _deviceHistoryService.Records.ToArray();

            Task<WanHistoryAggregate> wanTask = SafeQueryAsync(
                () => _historyRepository.GetWanAggregateAsync(
                    startUtc,
                    nowUtc,
                    cancellationToken),
                new WanHistoryAggregate(),
                cancellationToken);
            Task<RouterHealthAggregate> healthTask = SafeQueryAsync(
                () => _historyRepository.GetRouterHealthAggregateAsync(
                    startUtc,
                    nowUtc,
                    cancellationToken),
                new RouterHealthAggregate(),
                cancellationToken);
            Task<DeviceConnectionAggregate> connectionsTask = SafeQueryAsync(
                () => _historyRepository.GetDeviceConnectionAggregateAsync(
                    startUtc,
                    nowUtc,
                    cancellationToken),
                new DeviceConnectionAggregate(),
                cancellationToken);

            await Task.WhenAll(wanTask, healthTask, connectionsTask)
                .ConfigureAwait(false);

            WanHistoryAggregate wan = await wanTask.ConfigureAwait(false);
            RouterHealthAggregate health = await healthTask.ConfigureAwait(false);
            DeviceConnectionAggregate connections =
                await connectionsTask.ConfigureAwait(false);

            int newDevices = devices.Count(device =>
                device.FirstSeen.ToUniversalTime() >= startUtc &&
                device.FirstSeen.ToUniversalTime() <= nowUtc);
            int routerOfflineEvents = notifications.Count(notification =>
                IsWithinPeriod(notification.Timestamp, startUtc, nowUtc) &&
                (string.Equals(
                     notification.DeduplicationKey,
                     "RouterOffline",
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     notification.Title,
                     "Router Offline",
                     StringComparison.OrdinalIgnoreCase)));
            int adGuardDisabledEvents = notifications.Count(notification =>
                IsWithinPeriod(notification.Timestamp, startUtc, nowUtc) &&
                (string.Equals(
                     notification.DeduplicationKey,
                     "AdGuardProtectionDisabled",
                     StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(
                     notification.Title,
                     "AdGuard Protection Disabled",
                     StringComparison.OrdinalIgnoreCase)));

            IReadOnlyList<WeeklySummaryHighlight> highlights = BuildHighlights(
                wan,
                health,
                newDevices,
                routerOfflineEvents,
                adGuardDisabledEvents);

            _cachedSummary = new WeeklySummary
            {
                PeriodStartUtc = startUtc,
                PeriodEndUtc = nowUtc,
                TotalWanDownloadBytes = wan.TotalDownloadBytes,
                TotalWanUploadBytes = wan.TotalUploadBytes,
                AverageDownloadMbps = wan.AverageDownloadMbps,
                PeakDownloadMbps = wan.PeakDownloadMbps,
                AverageUploadMbps = wan.AverageUploadMbps,
                PeakUploadMbps = wan.PeakUploadMbps,
                AverageCpuPercent = health.AverageCpuPercent,
                PeakCpuPercent = health.PeakCpuPercent,
                AverageMemoryPercent = health.AverageMemoryPercent,
                PeakMemoryPercent = health.PeakMemoryPercent,
                NewDevicesCount = newDevices,
                ConnectionEventsCount = connections.EventCount,
                RouterOfflineEventsCount = routerOfflineEvents,
                AdGuardDisabledEventsCount = adGuardDisabledEvents,
                MostActiveNetworkName = connections.MostActiveNetworkName,
                Highlights = highlights,
                HistoricalDataPointCount =
                    wan.DataPointCount +
                    health.DataPointCount +
                    connections.EventCount +
                    newDevices
            };
            _cachedAtUtc = nowUtc;
            return _cachedSummary;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static async Task<T> SafeQueryAsync<T>(
        Func<Task<T>> query,
        T fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return fallback;
        }
    }

    private static IReadOnlyList<WeeklySummaryHighlight> BuildHighlights(
        WanHistoryAggregate wan,
        RouterHealthAggregate health,
        int newDevices,
        int routerOfflineEvents,
        int adGuardDisabledEvents)
    {
        var highlights = new List<WeeklySummaryHighlight>();
        if (newDevices > 0)
        {
            highlights.Add(new WeeklySummaryHighlight
            {
                Title = "New devices",
                Description = $"{newDevices} new device{(newDevices == 1 ? "" : "s")} joined this week.",
                Severity = InsightSeverity.Information,
                Category = InsightCategory.Device
            });
        }

        if (wan.PeakDownloadMbps.HasValue)
        {
            highlights.Add(new WeeklySummaryHighlight
            {
                Title = "Peak download",
                Description = $"Peak download speed was {wan.PeakDownloadMbps:0.00} Mbps.",
                Severity = InsightSeverity.Information,
                Category = InsightCategory.Internet
            });
        }

        if (health.PeakCpuPercent.HasValue)
        {
            highlights.Add(new WeeklySummaryHighlight
            {
                Title = "Router CPU",
                Description = $"Router CPU peaked at {health.PeakCpuPercent:0.0}%.",
                Severity = health.PeakCpuPercent >= 90
                    ? InsightSeverity.Warning
                    : InsightSeverity.Information,
                Category = InsightCategory.Router
            });
        }

        if (routerOfflineEvents > 0)
        {
            highlights.Add(new WeeklySummaryHighlight
            {
                Title = "Router connectivity",
                Description = $"Router was reported offline {routerOfflineEvents} time{(routerOfflineEvents == 1 ? "" : "s")}.",
                Severity = InsightSeverity.Warning,
                Category = InsightCategory.Router
            });
        }

        if (adGuardDisabledEvents > 0)
        {
            highlights.Add(new WeeklySummaryHighlight
            {
                Title = "AdGuard protection",
                Description = $"AdGuard protection was disabled {adGuardDisabledEvents} time{(adGuardDisabledEvents == 1 ? "" : "s")}.",
                Severity = InsightSeverity.Warning,
                Category = InsightCategory.AdGuard
            });
        }

        if (highlights.Count == 0 &&
            wan.DataPointCount + health.DataPointCount >= 2)
        {
            highlights.Add(new WeeklySummaryHighlight
            {
                Title = "Healthy week",
                Description = "No significant issues were detected this week.",
                Severity = InsightSeverity.Information,
                Category = InsightCategory.System
            });
        }

        return highlights.Take(5).ToArray();
    }

    private static bool IsWithinPeriod(
        DateTimeOffset timestamp,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc)
    {
        DateTimeOffset utc = timestamp.ToUniversalTime();
        return utc >= startUtc && utc <= endUtc;
    }
}
