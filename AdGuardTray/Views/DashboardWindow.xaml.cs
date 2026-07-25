using System.Windows.Threading;
using System;
using System.Threading.Tasks;
using System.Windows;
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

            _viewModel = new DashboardViewModel();

            DataContext = _viewModel;

            _settingsService =
                new SettingsService();


            Loaded += DashboardWindow_Loaded;


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


                if (string.IsNullOrWhiteSpace(settings.RouterIp) ||
                    string.IsNullOrWhiteSpace(settings.Username))
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



                //
                // Router Information
                //

                RouterInfo info =
                    await router.GetRouterInfoAsync();


                _viewModel.RouterConnected = true;

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



                //
                // AdGuard
                //

                AdGuardStatus adGuard =
                    await router.GetAdGuardStatusAsync();


                if (adGuard.ServiceStatus.StartsWith("SSH_"))
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

                //
                // AdGuard Statistics
                //

                AdGuardStatistics statistics =
                    await router.GetAdGuardStatisticsAsync();


                _viewModel.AdGuardQueries =
                    statistics.TotalQueries.ToString("N0");


                _viewModel.AdGuardBlocked =
                    statistics.BlockedQueries.ToString("N0");


                _viewModel.AdGuardBlockRate =
                    statistics.BlockPercentage
                        .ToString("0.0") + "%";

                //
                // Internet / Network
                //

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



                //
                // Dashboard status
                //

                _viewModel.StatusMessage =
                    "Connected";
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
            _viewModel.RouterConnected = false;

            _viewModel.InternetConnected = false;

            _viewModel.AdGuardRunning = false;


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


            _viewModel.WanIp =
                "-";

            _viewModel.Gateway =
                "-";

            _viewModel.ExternalDns = "-";

            _viewModel.AdvertisedDns = "-";

            _viewModel.Latency =
                "-";


            _viewModel.StatusMessage =
                message;


            _viewModel.LastRefresh =
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
                new SettingsWindow();

            settings.Owner = this;

            settings.ShowDialog();

            _ = RefreshDashboard();
        }


        protected override void OnClosed(EventArgs e)
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
            }

            base.OnClosed(e);
        }

    }
}