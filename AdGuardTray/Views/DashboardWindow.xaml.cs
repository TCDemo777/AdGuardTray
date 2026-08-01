using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AdGuardTray.Models;
using AdGuardTray.Services;
using AdGuardTray.ViewModels;
using Renci.SshNet.Common;

namespace AdGuardTray.Views
{
    public partial class DashboardWindow : Window
    {
        private readonly DashboardViewModel _viewModel;
        private readonly SettingsService _settingsService;
        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _trafficTimer;
        private bool _refreshInProgress;
        private bool _trafficRefreshInProgress;
        private RouterManager? _routerManager;
        private string? _routerSignature;

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

            _viewModel =
                new DashboardViewModel();

            DataContext =
                _viewModel;

            _settingsService =
                new SettingsService();

            Loaded +=
                DashboardWindow_Loaded;

            StateChanged +=
                DashboardWindow_StateChanged;

            IsVisibleChanged +=
                DashboardWindow_IsVisibleChanged;

            _refreshTimer =
                new DispatcherTimer
                {
                    Interval =
                        TimeSpan.FromSeconds(30)
                };

            _refreshTimer.Tick += async (s, e) =>
            {
                await RefreshDashboard();
            };

            _trafficTimer =
                new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };

            _trafficTimer.Tick += async (s, e) =>
            {
                await RefreshNetworkTrafficAsync();
            };

            ProtectionStateNotifier.StateChanged +=
                ProtectionStateNotifier_StateChanged;
        }

        private async void DashboardWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshDashboard();

            _refreshTimer.Start();

            if (IsVisible)
            {
                _trafficTimer.Start();
            }
        }

        private async Task RefreshDashboard()
        {
            if (_refreshInProgress)
            {
                return;
            }

            _refreshInProgress = true;

            try
            {
                AppSettings settings =
                    _settingsService.Load();

                int refreshSeconds =
                    Math.Clamp(
                        settings.RefreshIntervalSeconds,
                        5,
                        3600);

                _refreshTimer.Interval =
                    TimeSpan.FromSeconds(
                        refreshSeconds);

                if (string.IsNullOrWhiteSpace(
                        settings.RouterHost) ||
                    string.IsNullOrWhiteSpace(
                        settings.Username))
                {
                    ShowConnectionError(
                        "Router settings are incomplete.");

                    return;
                }

                string password =
                    _settingsService.DecryptPassword(
                        settings.EncryptedPassword);

                RouterManager router =
                    GetRouterManager(
                        settings,
                        password);

                RouterInfo info =
                    await router.GetRouterInfoAsync();

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

                if (adGuard.ServiceStatus.StartsWith(
                    "SSH_",
                    StringComparison.OrdinalIgnoreCase))
                {
                    ShowConnectionError(
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

                await Task.WhenAll(
                    statisticsTask,
                    rankingTask,
                    protectionTask);

                AdGuardStatistics statistics =
                    await statisticsTask;

                List<QueryLogEntry> rankingEntries =
                    await rankingTask;

                AdGuardProtectionStatus protectionStatus =
                    await protectionTask;

                _viewModel.UpdateAdGuardStatistics(
                    statistics);

                // Several AdGuard Home builds omit ranking arrays from
                // /control/stats. Build those Analytics lists from the
                // current query-log window whenever the stats lists are empty.
                _viewModel.UpdateRankingsFromQueryLog(
                    rankingEntries,
                    onlyWhenEmpty: false);

                // Protection state is authoritative from /control/status.
                // Statistics responses are not reliable for this value on
                // every GL.iNet AdGuard Home build.

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

                try
                {
                    List<WifiRadioInfo> wifiRadios =
                        await router.GetWifiRadiosAsync();

                    _viewModel.UpdateWifiRadios(wifiRadios);
                }
                catch
                {
                    // Wi-Fi discovery differs between GL.iNet/OpenWrt firmware
                    // builds. A discovery failure must not invalidate the main
                    // authenticated router session or the rest of the dashboard.
                    _viewModel.UpdateWifiRadios(Array.Empty<WifiRadioInfo>());
                }

                _viewModel.StatusMessage =
                    statistics.TotalQueries < 0
                        ? "Connected - AdGuard statistics unavailable"
                        : "Connected";

                _viewModel.RefreshStatusIndicators();

                _viewModel.LastRefresh =
                    "Last refresh: " +
                    DateTime.Now.ToString(
                        "dd MMM yyyy HH:mm:ss");
            }
            catch (SshAuthenticationException)
            {
                ShowConnectionError(
                    "SSH authentication failed.");
            }
            catch (SshConnectionException)
            {
                ShowConnectionError(
                    "Unable to connect to router.");
            }
            catch (Exception ex)
            {
                ShowConnectionError(
                    ex.Message);
            }
            finally
            {
                _refreshInProgress = false;
            }
        }

        private async Task RefreshNetworkTrafficAsync()
        {
            if (!IsVisible ||
                _trafficRefreshInProgress)
            {
                return;
            }

            _trafficRefreshInProgress = true;

            try
            {
                AppSettings settings = _settingsService.Load();

                if (string.IsNullOrWhiteSpace(settings.RouterHost) ||
                    string.IsNullOrWhiteSpace(settings.Username))
                {
                    return;
                }

                string password =
                    _settingsService.DecryptPassword(
                        settings.EncryptedPassword);

                RouterManager router =
                    GetRouterManager(
                        settings,
                        password);

                NetworkTrafficSnapshot snapshot =
                    await router.GetNetworkTrafficSnapshotAsync();

                if (!IsVisible)
                {
                    return;
                }

                UpdateNetworkTraffic(snapshot);
            }
            catch
            {
                // The main refresh reports connection errors. A missed live
                // traffic sample should not clear the rest of the dashboard.
            }
            finally
            {
                _trafficRefreshInProgress = false;
            }
        }

        private RouterManager GetRouterManager(
            AppSettings settings,
            string password)
        {
            string signature = string.Join(
                "|",
                settings.RouterHost.Trim(),
                settings.Username.Trim(),
                password);

            if (_routerManager is not null &&
                string.Equals(
                    _routerSignature,
                    signature,
                    StringComparison.Ordinal))
            {
                return _routerManager;
            }

            _routerManager?.Dispose();
            _routerManager = new RouterManager(
                settings.RouterHost,
                settings.Username,
                password);
            _routerSignature = signature;

            ResetTrafficStatistics();
            return _routerManager;
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

        private void UpdateNetworkTraffic(
            NetworkTrafficSnapshot snapshot)
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

            _previousTrafficSnapshot = snapshot;
        }

        public Task RefreshNowAsync()
        {
            return RefreshDashboard();
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

        private void DashboardWindow_IsVisibleChanged(
            object sender,
            DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                _previousTrafficSnapshot = null;
                _trafficBaselineRequired = true;

                if (IsLoaded &&
                    !_trafficTimer.IsEnabled)
                {
                    _trafficTimer.Start();
                }

                return;
            }

            _trafficTimer.Stop();
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

            _refreshTimer.Stop();
            _trafficTimer.Stop();
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

        private void ShowConnectionError(
            string message)
        {
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

        private async void Refresh_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshDashboard();
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
            PageContent.Content =
                new ProtectionView();

            SelectNavigationButton(
                ProtectionButton);
        }

        private void Analytics_Click(
            object sender,
            RoutedEventArgs e)
        {
            PageContent.Content =
                new AnalyticsView();

            SelectNavigationButton(
                AnalyticsButton);
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

        private void Clients_Click(
            object sender,
            RoutedEventArgs e)
        {
            PageContent.Content =
                new ClientsView();

            SelectNavigationButton(
                ClientsButton);
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
            PageContent.Content = new AboutView();
            SelectNavigationButton(AboutButton);
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
                ClientsButton,
                LogsButton,
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

        protected override void OnClosed(
            EventArgs e)
        {
            _refreshTimer.Stop();
            _trafficTimer.Stop();

            ProtectionStateNotifier.StateChanged -=
                ProtectionStateNotifier_StateChanged;

            _routerManager?.Dispose();
            _routerManager = null;

            base.OnClosed(e);
        }
    }
}
