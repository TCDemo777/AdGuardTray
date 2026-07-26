using System;
using System.Threading.Tasks;
using System.Windows;
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
                new DispatcherTimer();

            _refreshTimer.Interval =
                TimeSpan.FromSeconds(30);

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
                var settings =
                    _settingsService.Load();

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

                _viewModel.StorageUsage =
                    info.StorageUsage;

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

            _viewModel.StorageUsage =
                "-";

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

        private void Settings_Click(
            object sender,
            RoutedEventArgs e)
        {
            var settings =
                new SettingsWindow
                {
                    Owner = this
                };

            settings.ShowDialog();

            _ = RefreshDashboard();
        }

        private void Overview_Click(
            object sender,
            RoutedEventArgs e)
        {
            PageContent.Content =
                new OverviewView();

            OverviewButton.Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        53,
                        64,
                        77));

            OverviewButton.Foreground =
                Brushes.White;

            AnalyticsButton.Background =
                Brushes.Transparent;

            AnalyticsButton.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        215,
                        220,
                        226));
        }

        private void Analytics_Click(
            object sender,
            RoutedEventArgs e)
        {
            PageContent.Content =
                new AnalyticsView();

            AnalyticsButton.Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        53,
                        64,
                        77));

            AnalyticsButton.Foreground =
                Brushes.White;

            OverviewButton.Background =
                Brushes.Transparent;

            OverviewButton.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        215,
                        220,
                        226));
        }

        protected override void OnClosed(
            EventArgs e)
        {
            _refreshTimer.Stop();

            base.OnClosed(e);
        }
    }
}