using System;
using System.Collections.Generic;
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
        }

        private async void DashboardWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshDashboard();

            _refreshTimer.Start();
        }

        private async Task RefreshDashboard()
        {
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
                        settings.RouterIp) ||
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

                var router =
                    new RouterManager(
                        settings.RouterIp,
                        settings.Username,
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

                AdGuardStatistics statistics =
                    await router.GetAdGuardStatisticsAsync();

                _viewModel.UpdateAdGuardStatistics(
                    statistics);

                // Several AdGuard Home builds omit ranking arrays from
                // /control/stats. Build those Analytics lists from the
                // current query-log window whenever the stats lists are empty.
                List<QueryLogEntry> rankingEntries =
                    await router.GetQueryLogAsync();

                _viewModel.UpdateRankingsFromQueryLog(
                    rankingEntries,
                    onlyWhenEmpty: false);

                // Protection state is authoritative from /control/status.
                // Statistics responses are not reliable for this value on
                // every GL.iNet AdGuard Home build.
                AdGuardProtectionStatus protectionStatus =
                    await router.GetAdGuardProtectionStatusAsync();

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

        protected override void OnClosed(
            EventArgs e)
        {
            _refreshTimer.Stop();

            base.OnClosed(e);
        }
    }
}
