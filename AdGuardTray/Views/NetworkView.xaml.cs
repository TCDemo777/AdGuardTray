using System;
using System.Windows;
using System.Windows.Controls;
using AdGuardTray.Models;
using AdGuardTray.Services;

namespace AdGuardTray.Views
{
    public partial class NetworkView : UserControl
    {
        private readonly SettingsService _settingsService = new SettingsService();
        private bool _maintenanceInProgress;

        public NetworkView()
        {
            InitializeComponent();
        }

        private RouterManager CreateRouterManager()
        {
            AppSettings settings = _settingsService.Load();
            string password = _settingsService.DecryptPassword(settings.EncryptedPassword);
            return new RouterManager(settings.RouterIp, settings.Username, password);
        }

        private async void RestartWifi_Click(object sender, RoutedEventArgs e)
        {
            if (_maintenanceInProgress) return;
            if (MessageBox.Show(
                    "Restart Wi-Fi now? Wireless clients will disconnect briefly.",
                    "Restart Wi-Fi",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            await RunMaintenanceAsync(async router => await router.RestartWifiAsync());
        }

        private async void ReconnectWan_Click(object sender, RoutedEventArgs e)
        {
            if (_maintenanceInProgress) return;
            if (MessageBox.Show(
                    "Reconnect the WAN interface now? Internet access may pause briefly.",
                    "Reconnect WAN",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            await RunMaintenanceAsync(async router => await router.RestartWanAsync());
        }

        private async System.Threading.Tasks.Task RunMaintenanceAsync(
            Func<RouterManager, System.Threading.Tasks.Task<string>> operation)
        {
            _maintenanceInProgress = true;
            MaintenanceStatusText.Text = "Working…";
            IsEnabled = false;

            try
            {
                RouterManager router = CreateRouterManager();
                MaintenanceStatusText.Text = await operation(router);
            }
            catch (Exception ex)
            {
                MaintenanceStatusText.Text = "Operation failed: " + ex.Message;
            }
            finally
            {
                IsEnabled = true;
                _maintenanceInProgress = false;
            }
        }
    }
}
