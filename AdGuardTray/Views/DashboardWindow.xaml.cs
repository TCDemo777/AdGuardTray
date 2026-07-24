using System;
using System.Threading.Tasks;
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


            _viewModel =
                new DashboardViewModel();


            DataContext =
                _viewModel;


            _settingsService =
                new SettingsService();


            Loaded += DashboardWindow_Loaded;
        }





        private async void DashboardWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshDashboard();
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



                AdGuardTray.Models.AdGuardStatus adGuard =
                    await router.GetAdGuardStatusAsync();





                //
                // Check SSH failure returned by RouterManager
                //

                if (adGuard.ServiceStatus.Contains(
                        "SSH",
                        StringComparison.OrdinalIgnoreCase))
                {
                    ShowConnectionError(
                        adGuard.ServiceStatus);

                    return;
                }





                //
                // Router Connected
                //

                _viewModel.RouterConnected = true;


                _viewModel.RouterModel =
                    settings.RouterIp;


                _viewModel.FirmwareVersion =
                    "Connected";


                _viewModel.Uptime =
                    DateTime.Now.ToString(
                        "dd MMM yyyy HH:mm:ss");





                //
                // AdGuard Status
                //

                _viewModel.AdGuardRunning =
                    adGuard.IsRunning;


                _viewModel.AdGuardVersion =
                    adGuard.Version;


                _viewModel.AdGuardProcess =
                    adGuard.Process;


                _viewModel.AdGuardService =
                    adGuard.ServiceStatus;
            }





            catch (Renci.SshNet.Common.SshAuthenticationException)
            {
                ShowConnectionError(
                    "SSH authentication failed.\r\n\r\n" +
                    "Please check your username and password.");
            }





            catch (Renci.SshNet.Common.SshConnectionException)
            {
                ShowConnectionError(
                    "Unable to connect to router.");
            }





            catch (Exception ex)
            {
                ShowConnectionError(
                    "Unexpected error:\r\n\r\n" +
                    ex.Message);
            }
        }






        private void ShowConnectionError(
            string message)
        {
            _viewModel.RouterConnected =
                false;



            _viewModel.RouterModel =
                "Connection Failed";



            _viewModel.FirmwareVersion =
                message;



            _viewModel.Uptime =
                "";



            _viewModel.AdGuardRunning =
                false;



            _viewModel.AdGuardVersion =
                "";



            _viewModel.AdGuardProcess =
                "";



            _viewModel.AdGuardService =
                message;
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
            var settingsWindow =
                new SettingsWindow();



            settingsWindow.Owner =
                this;



            settingsWindow.ShowDialog();



            _ = RefreshDashboard();
        }
    }
}