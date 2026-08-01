using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AdGuardTray.Models;
using AdGuardTray.Services;
using AdGuardTray.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Renci.SshNet.Common;

namespace AdGuardTray.Views
{
    public partial class DashboardWindow : Window
    {
        private const string DashboardRefreshTask = "DashboardRefresh";
        private const string TrafficRefreshTask = "TrafficRefresh";
        private const string UpdateCheckTask = "UpdateCheck";

        private readonly DashboardViewModel _viewModel;
        private readonly SettingsService _settingsService;
        private readonly NotificationService _notificationService;
        private readonly NotificationCentreViewModel _notificationCentreViewModel;
        private readonly AdGuardProtectionNotificationTracker _protectionNotificationTracker;
        private readonly InsightEngine _insightEngine;
        private readonly DeviceHistoryService _deviceHistoryService;
        private readonly WanHistoryCollector _wanHistoryCollector;
        private readonly RouterHealthHistoryCollector _routerHealthHistoryCollector;
        private readonly WeeklySummaryService _weeklySummaryService;
        private readonly UpdateService _updateService;
        private readonly TimelineService _timelineService;
        private readonly IntelligenceService _intelligenceService;
        private readonly NetworkMapService _networkMapService;
        private readonly RefreshCoordinator _refreshCoordinator;
        private readonly SemaphoreSlim _routerManagerUsageGate = new(1, 1);
        private bool _refreshInProgress;
        private bool _trafficRefreshInProgress;
        private readonly IRouterManagerProvider _routerManagerProvider;
        private bool _routerOnline = true;
        private bool _forceWeeklySummaryRefresh;
        private int _diagnosticConnectedClientCount;
        private int _diagnosticNetworkCount;

        private NetworkTrafficSnapshot? _previousTrafficSnapshot;
        private bool _trafficBaselineRequired = true;
        private double _peakDownloadMbps;
        private double _peakUploadMbps;
        private double _downloadTotalMbps;
        private double _uploadTotalMbps;
        private int _trafficSampleCount;

        private readonly Brush _selectedNavigationBackground =
            new SolidColorBrush(
                Color.FromRgb(
                    53,
                    64,
                    77));

        private readonly Brush _unselectedNavigationForeground =
            new SolidColorBrush(
                Color.FromRgb(
                    215,
                    220,
                    226));

        public DashboardWindow()
        {
            InitializeComponent();

            HistoryRepository historyRepository = ((App)Application.Current)
                .Services.GetRequiredService<HistoryRepository>();
            _viewModel = new DashboardViewModel(
                ExecuteInsightActionAsync,
                historyRepository);

            DataContext =
                _viewModel;

            _notificationService = ((App)Application.Current)
                .Services.GetRequiredService<NotificationService>();
            _notificationCentreViewModel = ((App)Application.Current)
                .Services.GetRequiredService<NotificationCentreViewModel>();
            _protectionNotificationTracker = ((App)Application.Current)
                .Services.GetRequiredService<AdGuardProtectionNotificationTracker>();
            _insightEngine = ((App)Application.Current)
                .Services.GetRequiredService<InsightEngine>();
            _deviceHistoryService = ((App)Application.Current)
                .Services.GetRequiredService<DeviceHistoryService>();
            _wanHistoryCollector = ((App)Application.Current)
                .Services.GetRequiredService<WanHistoryCollector>();
            _routerHealthHistoryCollector = ((App)Application.Current)
                .Services.GetRequiredService<RouterHealthHistoryCollector>();
            _weeklySummaryService = ((App)Application.Current)
                .Services.GetRequiredService<WeeklySummaryService>();
            _updateService = ((App)Application.Current).Services
                .GetRequiredService<UpdateService>();
            _timelineService = ((App)Application.Current).Services
                .GetRequiredService<TimelineService>();
            _intelligenceService = ((App)Application.Current).Services
                .GetRequiredService<IntelligenceService>();
            _networkMapService = ((App)Application.Current).Services
                .GetRequiredService<NetworkMapService>();
            _routerManagerProvider = ((App)Application.Current).Services
                .GetRequiredService<IRouterManagerProvider>();
            NotificationButton.DataContext = _notificationService;

            _settingsService =
                new SettingsService();

            Loaded +=
                DashboardWindow_Loaded;

            StateChanged +=
                DashboardWindow_StateChanged;

            IsVisibleChanged +=
                DashboardWindow_IsVisibleChanged;

            _refreshCoordinator = new RefreshCoordinator();
            _refreshCoordinator.Register(
                DashboardRefreshTask,
                TimeSpan.FromSeconds(30),
                cancellationToken => RunOnUiThreadAsync(
                    () => RefreshDashboard(cancellationToken)),
                enabled: false);
            _refreshCoordinator.Register(
                TrafficRefreshTask,
                TimeSpan.FromSeconds(2),
                cancellationToken => RunOnUiThreadAsync(
                    () => RefreshNetworkTrafficAsync(cancellationToken)),
                enabled: false);
            _refreshCoordinator.Register(
                UpdateCheckTask,
                TimeSpan.FromDays(1),
                cancellationToken => _updateService.CheckForUpdatesAsync(
                    manual: false, cancellationToken),
                enabled: false);

            ProtectionStateNotifier.StateChanged +=
                ProtectionStateNotifier_StateChanged;
        }

        private async void DashboardWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await _refreshCoordinator.RunNowAsync(
                DashboardRefreshTask);

            await _refreshCoordinator.SetEnabledAsync(
                DashboardRefreshTask,
                true);

            if (IsVisible)
            {
                await _refreshCoordinator.SetEnabledAsync(
                    TrafficRefreshTask,
                    true);
            }

            await _refreshCoordinator.RunNowAsync(UpdateCheckTask);
            await _refreshCoordinator.SetEnabledAsync(UpdateCheckTask, true);
        }

        private async Task RefreshDashboard(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_refreshInProgress)
            {
                return;
            }

            _refreshInProgress = true;
            bool routerCommunicationConfirmed = false;
            bool routerManagerGateEntered = false;

            try
            {
                await _routerManagerUsageGate.WaitAsync(cancellationToken);
                routerManagerGateEntered = true;

                AppSettings settings =
                    _settingsService.Load();

                int refreshSeconds =
                    Math.Clamp(
                        settings.RefreshIntervalSeconds,
                        5,
                        3600);

                _refreshCoordinator.UpdateInterval(
                    DashboardRefreshTask,
                    TimeSpan.FromSeconds(
                        refreshSeconds));

                if (string.IsNullOrWhiteSpace(
                        settings.RouterHost) ||
                    string.IsNullOrWhiteSpace(
                        settings.Username))
                {
                    await ShowConnectionErrorAsync(
                        "Router settings are incomplete.",
                        notifyConnectivityChange: false);

                    return;
                }

                RouterManager router =
                    await _routerManagerProvider.GetRouterManagerAsync(
                        cancellationToken);

                RouterInfo info =
                    await router.GetRouterInfoAsync();

                cancellationToken.ThrowIfCancellationRequested();
                routerCommunicationConfirmed = true;

                _viewModel.RouterConnected =
                    true;

                _viewModel.RouterModel =
                    info.Model;

                _viewModel.Hostname =
                    info.Hostname;

                _viewModel.FirmwareVersion =
                    info.Firmware;

                _viewModel.Uptime =
                    info.Uptime;

                _viewModel.CpuUsage =
                    info.CpuUsage;

                _viewModel.Temperature =
                    info.Temperature;

                _viewModel.LoadAverage =
                    info.LoadAverage;

                _viewModel.MemoryUsage =
                    info.MemoryUsage;

                _viewModel.UpdateStorageUsage(
                    info.StorageUsage);

                AdGuardStatus adGuard =
                    await router.GetAdGuardStatusAsync();

                cancellationToken.ThrowIfCancellationRequested();
                if (adGuard.ServiceStatus.StartsWith(
                    "SSH_",
                    StringComparison.OrdinalIgnoreCase))
                {
                    await ShowConnectionErrorAsync(
                        adGuard.ServiceStatus);

                    return;
                }

                _viewModel.AdGuardRunning =
                    adGuard.IsRunning;

                _viewModel.AdGuardVersion =
                    adGuard.Version;

                _viewModel.AdGuardProcess =
                    adGuard.Process;

                _viewModel.AdGuardService =
                    adGuard.ServiceStatus;

                // These requests use the pooled AdGuard HTTP client and are
                // independent, so run them together instead of serially.
                Task<AdGuardStatistics> statisticsTask =
                    router.GetAdGuardStatisticsAsync();

                Task<List<QueryLogEntry>> rankingTask =
                    router.GetQueryLogAsync();

                Task<AdGuardProtectionStatus> protectionTask =
                    router.GetAdGuardProtectionStatusAsync();

                Task allAdGuardTasks = Task.WhenAll(
                    statisticsTask,
                    rankingTask,
                    protectionTask);

                try
                {
                    AdGuardProtectionStatus protectionStatus =
                        await protectionTask;

                    cancellationToken.ThrowIfCancellationRequested();
                    await _protectionNotificationTracker.ProcessProtectionStateAsync(
                        protectionStatus.IsEnabled,
                        ProtectionStateSource.Refresh);

                    cancellationToken.ThrowIfCancellationRequested();

                    // Protection state is authoritative from /control/status.
                    // Keep processing it before applying the other results.
                    _viewModel.AdGuardProtectionEnabled =
                        protectionStatus.IsEnabled;

                    _viewModel.AdGuardProtectionPaused =
                        protectionStatus.IsPaused;

                    _viewModel.AdGuardProtectionStatusKnown =
                        true;

                    _viewModel.AdGuardProtectionRemaining =
                        protectionStatus.IsPaused
                            ? FormatProtectionRemaining(
                                protectionStatus.RemainingPause)
                            : "";

                    await allAdGuardTasks;
                }
                catch
                {
                    await ObserveTaskAsync(allAdGuardTasks);
                    throw;
                }

                cancellationToken.ThrowIfCancellationRequested();

                AdGuardStatistics statistics =
                    await statisticsTask;

                List<QueryLogEntry> rankingEntries =
                    await rankingTask;

                _viewModel.UpdateAdGuardStatistics(
                    statistics);

                // Several AdGuard Home builds omit ranking arrays from
                // /control/stats. Build those Analytics lists from the
                // current query-log window whenever the stats lists are empty.
                _viewModel.UpdateRankingsFromQueryLog(
                    rankingEntries,
                    onlyWhenEmpty: false);

                if (statistics.TotalQueries < 0 ||
                    statistics.BlockedQueries < 0)
                {
                    _viewModel.AdGuardQueries =
                        "-";

                    _viewModel.AdGuardBlocked =
                        "-";

                    _viewModel.AdGuardBlockRate =
                        "-";
                }
                else
                {
                    _viewModel.AdGuardQueries =
                        statistics.TotalQueries
                            .ToString("N0");

                    _viewModel.AdGuardBlocked =
                        statistics.BlockedQueries
                            .ToString("N0");

                    _viewModel.AdGuardBlockRate =
                        statistics.BlockPercentage
                            .ToString("0.0") + "%";
                }

                NetworkInfo network =
                    await router.GetNetworkInfoAsync();

                cancellationToken.ThrowIfCancellationRequested();
                _viewModel.InternetConnected =
                    network.Connected;

                _viewModel.WanIp =
                    network.WanIp;

                _viewModel.Gateway =
                    network.Gateway;

                _viewModel.ExternalDns =
                    network.ExternalDns;

                _viewModel.AdvertisedDns =
                    network.AdvertisedDns;

                _viewModel.Latency =
                    network.Latency;

                List<WifiRadioInfo> wifiRadios;
                try
                {
                    wifiRadios =
                        await router.GetWifiRadiosAsync();

                    cancellationToken.ThrowIfCancellationRequested();
                    _viewModel.UpdateWifiRadios(wifiRadios);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Wi-Fi discovery differs between GL.iNet/OpenWrt firmware
                    // builds. A discovery failure must not invalidate the main
                    // authenticated router session or the rest of the dashboard.
                    wifiRadios = new List<WifiRadioInfo>();
                    _viewModel.UpdateWifiRadios(wifiRadios);
                }

                _viewModel.StatusMessage =
                    statistics.TotalQueries < 0
                        ? "Connected - AdGuard statistics unavailable"
                        : "Connected";

                await UpdateRouterConnectivityAsync(isOnline: true);

                _viewModel.RefreshStatusIndicators();

                IReadOnlyCollection<DeviceHistoryRecord> deviceHistory =
                    _deviceHistoryService.Records;
                var connectedMacAddresses = deviceHistory
                    .Where(record => record.IsCurrentlyOnline)
                    .Select(record => record.MacAddress)
                    .Concat(wifiRadios
                        .SelectMany(radio => radio.Clients)
                        .Where(client =>
                            client.IsActiveStation ||
                            (client.IsOnlineStateKnown &&
                             client.IsCurrentlyOnline))
                        .Select(client =>
                            DeviceHistoryService.NormalizeMacAddress(
                                client.MacAddress)))
                    .Where(mac => mac.Length == 12)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                _diagnosticConnectedClientCount = connectedMacAddresses.Count;
                _diagnosticNetworkCount = wifiRadios.Count;

                var insightContext = new InsightContext
                {
                    EvaluatedAt = DateTimeOffset.Now,
                    RouterConnected = true,
                    RouterHealth = info,
                    CpuPercentage = _viewModel.CpuPercentage,
                    MemoryPercentage = _viewModel.MemoryPercentage,
                    StoragePercentage = _viewModel.StoragePercentage,
                    WanStatus = network,
                    AdGuardStatus = adGuard,
                    AdGuardProtectionStatusKnown =
                        _viewModel.AdGuardProtectionStatusKnown,
                    AdGuardProtectionEnabled =
                        _viewModel.AdGuardProtectionEnabled,
                    DnsStatistics = statistics,
                    DeviceHistory = deviceHistory,
                    NotificationHistory =
                        _notificationService.Notifications.ToArray(),
                    ConnectedClientMacAddresses = connectedMacAddresses,
                    ConnectedClientSnapshotComplete =
                        _deviceHistoryService.HasCompleteSnapshot
                };

                IReadOnlyList<Insight> insights =
                    await _insightEngine.EvaluateAsync(
                        insightContext,
                        cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _viewModel.UpdateInsights(insights);
                _timelineService.RecordInsights(insights);

                IReadOnlyList<BehaviourObservation> observations =
                    await _intelligenceService.AnalyzeAsync(
                        cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _viewModel.UpdateIntelligenceObservations(observations);
                IReadOnlyDictionary<string, DeviceBehaviourProfile> behaviourProfiles =
                    await _intelligenceService.GetDeviceProfilesAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _networkMapService.UpdateTopology(
                    _viewModel.InternetConnected,
                    _viewModel.RouterConnected,
                    _viewModel.RouterModel,
                    deviceHistory,
                    wifiRadios,
                    behaviourProfiles);

                try
                {
                    await _routerHealthHistoryCollector.RecordSnapshotAsync(
                        DateTimeOffset.UtcNow,
                        ParsePercentage(info.CpuUsage),
                        ParsePercentage(info.MemoryUsage),
                        ParseByteSize(info.MemoryUsed),
                        memoryTotalBytes: null,
                        ParseTemperature(info.Temperature),
                        ParseStoragePercentage(info.StorageUsage),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Historical persistence must not invalidate a completed
                    // dashboard refresh.
                }

                bool forceWeeklySummary = _forceWeeklySummaryRefresh;
                _forceWeeklySummaryRefresh = false;
                try
                {
                    WeeklySummary weeklySummary =
                        await _weeklySummaryService.GetSummaryAsync(
                            forceWeeklySummary,
                            cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    _viewModel.UpdateWeeklySummary(weeklySummary);
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // A partial or unavailable summary must not invalidate
                    // an otherwise successful dashboard refresh.
                }

                _viewModel.LastRefresh =
                    "Last refresh: " +
                    DateTime.Now.ToString(
                        "dd MMM yyyy HH:mm:ss");
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (SshAuthenticationException)
            {
                await ShowConnectionErrorAsync(
                    "SSH authentication failed.");
            }
            catch (SshConnectionException)
            {
                await ShowConnectionErrorAsync(
                    "Unable to connect to router.");
            }
            catch (Exception ex)
            {
                await ShowConnectionErrorAsync(
                    ex.Message,
                    notifyConnectivityChange: !routerCommunicationConfirmed);
            }
            finally
            {
                if (routerManagerGateEntered)
                {
                    _routerManagerUsageGate.Release();
                }

                _refreshInProgress = false;
            }
        }

        private async Task RefreshNetworkTrafficAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsVisible ||
                _trafficRefreshInProgress)
            {
                return;
            }

            _trafficRefreshInProgress = true;
            bool routerManagerGateEntered = false;

            try
            {
                await _routerManagerUsageGate.WaitAsync(cancellationToken);
                routerManagerGateEntered = true;

                AppSettings settings = _settingsService.Load();

                if (string.IsNullOrWhiteSpace(settings.RouterHost) ||
                    string.IsNullOrWhiteSpace(settings.Username))
                {
                    return;
                }

                RouterManager router =
                    await _routerManagerProvider.GetRouterManagerAsync(
                        cancellationToken);

                NetworkTrafficSnapshot snapshot =
                    await router.GetNetworkTrafficSnapshotAsync();

                cancellationToken.ThrowIfCancellationRequested();
                if (!IsVisible)
                {
                    return;
                }

                await UpdateNetworkTrafficAsync(snapshot, cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
            catch
            {
                // The main refresh reports connection errors. A missed live
                // traffic sample should not clear the rest of the dashboard.
            }
            finally
            {
                if (routerManagerGateEntered)
                {
                    _routerManagerUsageGate.Release();
                }

                _trafficRefreshInProgress = false;
            }
        }

        private static async Task ObserveTaskAsync(Task task)
        {
            try
            {
                await task;
            }
            catch
            {
                // The original exception is rethrown by the caller.
            }
        }

        private void ResetTrafficStatistics()
        {
            _previousTrafficSnapshot = null;
            _trafficBaselineRequired = true;
            _peakDownloadMbps = 0;
            _peakUploadMbps = 0;
            _downloadTotalMbps = 0;
            _uploadTotalMbps = 0;
            _trafficSampleCount = 0;
        }

        private async Task UpdateNetworkTrafficAsync(
            NetworkTrafficSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            if (_trafficBaselineRequired ||
                _previousTrafficSnapshot == null)
            {
                _previousTrafficSnapshot = snapshot;
                _trafficBaselineRequired = false;
                return;
            }

            if (snapshot.ReceivedBytes <
                    _previousTrafficSnapshot.ReceivedBytes ||
                snapshot.TransmittedBytes <
                    _previousTrafficSnapshot.TransmittedBytes)
            {
                _previousTrafficSnapshot = snapshot;
                return;
            }

            double elapsedSeconds =
                Math.Max(
                    0.25,
                    (snapshot.CapturedAtUtc -
                     _previousTrafficSnapshot.CapturedAtUtc)
                    .TotalSeconds);

            long receivedDelta =
                snapshot.ReceivedBytes -
                _previousTrafficSnapshot.ReceivedBytes;

            long transmittedDelta =
                snapshot.TransmittedBytes -
                _previousTrafficSnapshot.TransmittedBytes;

            double downloadMbps =
                Math.Max(
                    0,
                    receivedDelta * 8d /
                    elapsedSeconds /
                    1_000_000d);

            double uploadMbps =
                Math.Max(
                    0,
                    transmittedDelta * 8d /
                    elapsedSeconds /
                    1_000_000d);

            _peakDownloadMbps =
                Math.Max(_peakDownloadMbps, downloadMbps);

            _peakUploadMbps =
                Math.Max(_peakUploadMbps, uploadMbps);

            _downloadTotalMbps += downloadMbps;
            _uploadTotalMbps += uploadMbps;
            _trafficSampleCount++;

            _viewModel.UpdateNetworkTraffic(
                downloadMbps,
                uploadMbps,
                _peakDownloadMbps,
                _peakUploadMbps,
                _downloadTotalMbps / _trafficSampleCount,
                _uploadTotalMbps / _trafficSampleCount,
                snapshot.InterfaceName);

            try
            {
                await _wanHistoryCollector.RecordSampleAsync(
                    new DateTimeOffset(
                        DateTime.SpecifyKind(
                            snapshot.CapturedAtUtc,
                            DateTimeKind.Utc)),
                    downloadMbps,
                    uploadMbps,
                    snapshot.ReceivedBytes,
                    snapshot.TransmittedBytes,
                    cancellationToken);
            }

            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Historical persistence must not interrupt the live graph.
            }

            _previousTrafficSnapshot = snapshot;
        }

        public Task RefreshNowAsync()
        {
            _forceWeeklySummaryRefresh = true;
            return _refreshCoordinator
                .RunNowAsync(DashboardRefreshTask);
        }

        private void DashboardWindow_StateChanged(
            object? sender,
            EventArgs e)
        {
            if (WindowState == WindowState.Minimized &&
                Application.Current is App app)
            {
                Dispatcher.BeginInvoke(
                    new Action(app.HideDashboard));
            }
        }

        private async void DashboardWindow_IsVisibleChanged(
            object sender,
            DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                _previousTrafficSnapshot = null;
                _trafficBaselineRequired = true;

                if (IsLoaded)
                {
                    await _refreshCoordinator.SetEnabledAsync(
                        TrafficRefreshTask,
                        true);
                }

                return;
            }

            await _refreshCoordinator.SetEnabledAsync(
                TrafficRefreshTask,
                false);
        }

        protected override void OnClosing(
            CancelEventArgs e)
        {
            if (Application.Current is App app &&
                !app.IsExitRequested)
            {
                e.Cancel = true;
                app.HideDashboard();
                return;
            }

            base.OnClosing(e);
        }

        private static string FormatProtectionRemaining(
            TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                return "Less than a minute remaining";
            }

            if (duration.TotalDays >= 1)
            {
                return $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m remaining";
            }

            if (duration.TotalHours >= 1)
            {
                return $"{(int)duration.TotalHours}h {duration.Minutes}m remaining";
            }

            return $"{Math.Max(1, duration.Minutes)}m remaining";
        }

        private async Task ShowConnectionErrorAsync(
            string message,
            bool notifyConnectivityChange = true)
        {
            if (notifyConnectivityChange)
                await UpdateRouterConnectivityAsync(isOnline: false);

            _viewModel.RouterConnected =
                false;

            _viewModel.InternetConnected =
                false;

            _viewModel.AdGuardRunning =
                false;

            _viewModel.ClearAdGuardStatistics();

            _viewModel.RouterModel =
                "Connection Failed";

            _viewModel.Hostname =
                "-";

            _viewModel.FirmwareVersion =
                "-";

            _viewModel.Uptime =
                "-";

            _viewModel.Temperature =
                "-";

            _viewModel.LoadAverage =
                "-";

            _viewModel.CpuUsage =
                "-";

            _viewModel.MemoryUsage =
                "-";

            _viewModel.UpdateStorageUsage(
                null);

            _viewModel.AdGuardVersion =
                "-";

            _viewModel.AdGuardProcess =
                "-";

            _viewModel.AdGuardService =
                "-";

            _viewModel.AdGuardQueries =
                "-";

            _viewModel.AdGuardBlocked =
                "-";

            _viewModel.AdGuardBlockRate =
                "-";

            _viewModel.WanIp =
                "-";

            _viewModel.Gateway =
                "-";

            _viewModel.ExternalDns =
                "-";

            _viewModel.AdvertisedDns =
                "-";

            _viewModel.Latency =
                "-";

            ResetTrafficStatistics();
            _viewModel.ClearNetworkTraffic();

            _viewModel.StatusMessage =
                message;

            _viewModel.RefreshStatusIndicators();

            _viewModel.LastRefresh =
                "Last refresh: " +
                DateTime.Now.ToString(
                    "dd MMM yyyy HH:mm:ss");
        }

        private async Task UpdateRouterConnectivityAsync(bool isOnline)
        {
            if (_routerOnline == isOnline)
                return;

            _routerOnline = isOnline;

            await _notificationService.AddAsync(new AppNotification
            {
                Title = isOnline
                    ? "Router Online"
                    : "Router Offline",
                Message = isOnline
                    ? "Connection to the router has been restored."
                    : "Unable to communicate with the configured router.",
                Severity = isOnline
                    ? NotificationSeverity.Success
                    : NotificationSeverity.Error,
                Category = NotificationCategory.Router,
                DeduplicationKey = isOnline
                    ? "RouterOnline"
                    : "RouterOffline"
            });
        }

        public Task PrepareForShutdownAsync()
        {
            return _refreshCoordinator.DisposeAsync().AsTask();
        }

        private async void Refresh_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshNowAsync();
        }

        private void Overview_Click(
            object sender,
            RoutedEventArgs e)
        {
            PageContent.Content =
                new OverviewView();

            SelectNavigationButton(
                OverviewButton);
        }

        private void Protection_Click(
            object sender,
            RoutedEventArgs e)
        {
            NavigateToProtection();
        }

        private void Analytics_Click(
            object sender,
            RoutedEventArgs e)
        {
            NavigateToAnalytics();
        }

        private void Network_Click(
            object sender,
            RoutedEventArgs e)
        {
            PageContent.Content =
                new NetworkView();

            SelectNavigationButton(
                NetworkButton);
        }

        private void NetworkMap_Click(object sender, RoutedEventArgs e)
        {
            PageContent.Content = new NetworkMapView();
            SelectNavigationButton(NetworkMapButton);
        }

        private void Clients_Click(
            object sender,
            RoutedEventArgs e)
        {
            NavigateToClients();
        }

        private void Logs_Click(
            object sender,
            RoutedEventArgs e)
        {
            PageContent.Content =
                new LogsView();

            SelectNavigationButton(
                LogsButton);
        }

        private void Search_Click(
            object sender,
            RoutedEventArgs e)
        {
            PageContent.Content =
                new GlobalSearchView();

            SelectNavigationButton(
                SearchButton);
        }

        private void Notification_Click(
            object sender,
            RoutedEventArgs e)
        {
            NavigateToNotifications();
        }

        private void Timeline_Click(object sender, RoutedEventArgs e)
        {
            PageContent.Content = new TimelineView();
            SelectNavigationButton(TimelineButton);
        }

        private void NavigateToProtection()
        {
            PageContent.Content = new ProtectionView();
            SelectNavigationButton(ProtectionButton);
        }

        private void NavigateToAnalytics()
        {
            PageContent.Content = new AnalyticsView();
            SelectNavigationButton(AnalyticsButton);
        }

        private void NavigateToClients()
        {
            PageContent.Content = new ClientsView();
            SelectNavigationButton(ClientsButton);
        }

        private void NavigateToNotifications()
        {
            PageContent.Content =
                new NotificationCentreView(_notificationCentreViewModel);
            SelectNavigationButton(NotificationButton);
        }

        private async Task ExecuteInsightActionAsync(Insight insight)
        {
            switch (insight.Action)
            {
                case InsightActionKind.RebootRouter:
                    await RebootRouterFromInsightAsync();
                    break;

                case InsightActionKind.EnableProtection:
                    NavigateToProtection();
                    ProtectionViewModel protectionViewModel =
                        ((App)Application.Current).Services
                            .GetRequiredService<ProtectionViewModel>();
                    if (protectionViewModel.EnableProtectionCommand.CanExecute(null))
                    {
                        await protectionViewModel.EnableProtectionCommand
                            .ExecuteAsync(null);
                    }
                    break;

                case InsightActionKind.ViewNotifications:
                    NavigateToNotifications();
                    break;

                case InsightActionKind.OpenClients:
                    NavigateToClients();
                    break;

                case InsightActionKind.ViewAnalytics:
                    NavigateToAnalytics();
                    break;
            }
        }

        private static double? ParsePercentage(string? value)
        {
            if (IsPlaceholder(value))
                return null;

            Match match = Regex.Match(value!, @"\d+(?:[\.,]\d+)?");
            if (!match.Success || !TryParseNumber(match.Value, out double percent))
                return null;

            return percent is >= 0 and <= 100 ? percent : null;
        }

        private static double? ParseTemperature(string? value)
        {
            if (IsPlaceholder(value))
                return null;

            Match match = Regex.Match(value!, @"-?\d+(?:[\.,]\d+)?");
            return match.Success && TryParseNumber(match.Value, out double temperature)
                ? temperature
                : null;
        }

        private static double? ParseStoragePercentage(string? value)
        {
            if (IsPlaceholder(value))
                return null;

            string[] lines = value!
                .Replace("\r", string.Empty)
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);
            string? candidate = lines.FirstOrDefault(line =>
                line.Contains("/overlay", StringComparison.OrdinalIgnoreCase) ||
                line.EndsWith(" /", StringComparison.OrdinalIgnoreCase))
                ?? lines.LastOrDefault();
            if (string.IsNullOrWhiteSpace(candidate))
                return null;

            Match match = Regex.Match(candidate, @"(\d+(?:[\.,]\d+)?)%");
            if (!match.Success ||
                !TryParseNumber(match.Groups[1].Value, out double percent))
            {
                return null;
            }

            return percent is >= 0 and <= 100 ? percent : null;
        }

        private static long? ParseByteSize(string? value)
        {
            if (IsPlaceholder(value))
                return null;

            Match match = Regex.Match(
                value!,
                @"(?i)(\d+(?:[\.,]\d+)?)\s*(B|KB|MB|GB|TB)");
            if (!match.Success ||
                !TryParseNumber(match.Groups[1].Value, out double amount))
            {
                return null;
            }

            double multiplier = match.Groups[2].Value.ToUpperInvariant() switch
            {
                "KB" => 1024d,
                "MB" => 1024d * 1024d,
                "GB" => 1024d * 1024d * 1024d,
                "TB" => 1024d * 1024d * 1024d * 1024d,
                _ => 1d
            };
            double bytes = amount * multiplier;
            return bytes is >= 0 and <= long.MaxValue ? (long)bytes : null;
        }

        private static bool TryParseNumber(string value, out double result) =>
            double.TryParse(
                value.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result);

        private static bool IsPlaceholder(string? value) =>
            string.IsNullOrWhiteSpace(value) ||
            value.Trim() == "-" ||
            value.Contains("Unknown", StringComparison.OrdinalIgnoreCase);

        private async Task RebootRouterFromInsightAsync()
        {
            if (MessageBox.Show(
                    "Reboot the router now? Network access will be interrupted temporarily.",
                    "Reboot Router",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            bool gateEntered = false;
            try
            {
                await _routerManagerUsageGate.WaitAsync();
                gateEntered = true;
                RouterManager router =
                    await _routerManagerProvider.GetRouterManagerAsync();
                await router.RebootRouterAsync();
                _viewModel.StatusMessage = "Router reboot requested.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to reboot the router: " + ex.Message,
                    "Reboot Router",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (gateEntered)
                    _routerManagerUsageGate.Release();
            }
        }

        private void NavigationSettings_Click(
            object sender,
            RoutedEventArgs e)
        {
            PageContent.Content =
                new SettingsView();

            SelectNavigationButton(
                NavigationSettingsButton);
        }

        private void About_Click(
            object sender,
            RoutedEventArgs e)
        {
            PageContent.Content = new AboutView(CreateDiagnosticRuntimeState);
            SelectNavigationButton(AboutButton);
        }

        private DiagnosticRuntimeState CreateDiagnosticRuntimeState()
        {
            return new DiagnosticRuntimeState
            {
                RouterOnline = _viewModel.RouterConnected,
                InternetOnline = _viewModel.InternetConnected,
                RouterModel = _viewModel.RouterModel,
                FirmwareVersion = _viewModel.FirmwareVersion,
                AdGuardProtectionEnabled = _viewModel.AdGuardProtectionStatusKnown
                    ? _viewModel.AdGuardProtectionEnabled
                    : null,
                CpuPercent = _viewModel.CpuPercentage is >= 0 and <= 100
                    ? _viewModel.CpuPercentage : null,
                MemoryPercent = _viewModel.MemoryPercentage is >= 0 and <= 100
                    ? _viewModel.MemoryPercentage : null,
                StoragePercent = _viewModel.StoragePercentage is >= 0 and <= 100
                    ? _viewModel.StoragePercentage : null,
                Temperature = _viewModel.Temperature,
                DownloadRate = _viewModel.CurrentDownload,
                UploadRate = _viewModel.CurrentUpload,
                ConnectedClientCount = _diagnosticConnectedClientCount,
                NetworkCount = _diagnosticNetworkCount,
                NotificationUnreadCount = _notificationService.UnreadCount,
                RefreshTasks = _refreshCoordinator.GetDiagnosticState()
            };
        }

        private void SelectNavigationButton(
            Button selectedButton)
        {
            Button[] navigationButtons =
            {
                OverviewButton,
                ProtectionButton,
                AnalyticsButton,
                NetworkButton,
                NetworkMapButton,
                ClientsButton,
                LogsButton,
                NotificationButton,
                TimelineButton,
                SearchButton,
                NavigationSettingsButton,
                AboutButton
            };

            foreach (Button button in navigationButtons)
            {
                bool isSelected =
                    button == selectedButton;

                button.Background =
                    isSelected
                        ? _selectedNavigationBackground
                        : Brushes.Transparent;

                button.Foreground =
                    isSelected
                        ? Brushes.White
                        : _unselectedNavigationForeground;
            }
        }

        private void ProtectionStateNotifier_StateChanged(
            object? sender,
            AdGuardProtectionStatus status)
        {
            void ApplyState()
            {
                _viewModel.AdGuardProtectionEnabled =
                    status.IsEnabled;

                _viewModel.AdGuardProtectionPaused =
                    status.IsPaused;

                _viewModel.AdGuardProtectionStatusKnown =
                    true;

                _viewModel.AdGuardProtectionRemaining =
                    status.IsPaused
                        ? FormatProtectionRemaining(
                            status.RemainingPause)
                        : "";

                _viewModel.RefreshStatusIndicators();

                _viewModel.LastRefresh =
                    "Protection updated: " +
                    DateTime.Now.ToString(
                        "dd MMM yyyy HH:mm:ss");
            }

            if (Dispatcher.CheckAccess())
            {
                ApplyState();
            }
            else
            {
                Dispatcher.Invoke(ApplyState);
            }
        }

        protected override async void OnClosed(
            EventArgs e)
        {
            await PrepareForShutdownAsync();

            ProtectionStateNotifier.StateChanged -=
                ProtectionStateNotifier_StateChanged;

            _routerManagerUsageGate.Dispose();

            base.OnClosed(e);
        }

        private Task RunOnUiThreadAsync(Func<Task> callback)
        {
            if (Dispatcher.CheckAccess())
            {
                return callback();
            }

            return Dispatcher
                .InvokeAsync(callback)
                .Task
                .Unwrap();
        }
    }
}
