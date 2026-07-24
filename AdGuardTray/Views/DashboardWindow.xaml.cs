using System;
using System.Windows;
using AdGuardTray.Services;
using AdGuardTray.ViewModels;

namespace AdGuardTray.Views
{
    public partial class DashboardWindow : Window
    {
        private readonly DashboardViewModel _viewModel;

        private readonly SettingsService _settingsService;

        public DashboardWindow()
        {
            InitializeComponent();

            _viewModel = new DashboardViewModel();

            DataContext = _viewModel;

            _settingsService = new SettingsService();

            Loaded += DashboardWindow_Loaded;
        }

        private async void DashboardWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshDashboard();
        }

        private async System.Threading.Tasks.Task RefreshDashboard()
        {
            try
            {
                var settings =
                    _settingsService.Load();

                var router =
                    new RouterManager(
                        settings.RouterIp,
                        settings.Username,
                        _settingsService.DecryptPassword(
                            settings.EncryptedPassword));

                var adGuard =
                    await router.GetAdGuardStatusAsync();

                _viewModel.RouterConnected = true;

                _viewModel.RouterModel =
                    settings.RouterIp;

                _viewModel.FirmwareVersion =
                    "Connected";

                _viewModel.Uptime =
                    DateTime.Now.ToString(
                        "dd MMM yyyy HH:mm:ss");

                _viewModel.AdGuardRunning =
                    adGuard.IsRunning;

                _viewModel.AdGuardVersion =
                    adGuard.Version;
            }
            catch (Exception ex)
            {
                _viewModel.RouterConnected = false;

                _viewModel.RouterModel =
                    "Connection Failed";

                _viewModel.FirmwareVersion =
                    ex.Message;

                _viewModel.Uptime = "";

                _viewModel.AdGuardRunning = false;

                _viewModel.AdGuardVersion = "";
            }
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

            settings.ShowDialog();
        }
    }
}