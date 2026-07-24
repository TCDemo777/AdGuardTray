using System.Windows;
using AdGuardTray.Models;
using AdGuardTray.Services;

namespace AdGuardTray.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;

        public SettingsWindow()
        {
            InitializeComponent();

            _settingsService = new SettingsService();

            LoadSettings();

            SaveButton.Click += SaveButton_Click;
        }


        private void LoadSettings()
        {
            AppSettings settings = _settingsService.Load();

            RouterIpBox.Text = settings.RouterIp;

            UsernameBox.Text = settings.Username;

            PasswordBox.Password =
                _settingsService.DecryptPassword(settings.EncryptedPassword);

            RememberPasswordCheck.IsChecked =
                settings.RememberPassword;

            StartWithWindowsCheck.IsChecked =
                settings.StartWithWindows;

            StatusText.Text = "Status: Ready";
        }


        private async void TestAdGuardButton_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Status: Testing AdGuard...";

            bool connected = await App.AdGuard.IsAvailableAsync(
                "http://192.168.1.1:3000/"
            );

            StatusText.Text = connected
                ? "Status: AdGuard Home reachable."
                : "Status: Cannot reach AdGuard Home.";
        }


        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = new AppSettings
            {
                RouterIp = RouterIpBox.Text.Trim(),

                Username = UsernameBox.Text.Trim(),

                RememberPassword = RememberPasswordCheck.IsChecked == true,

                StartWithWindows = StartWithWindowsCheck.IsChecked == true
            };

            if (settings.RememberPassword)
            {
                settings.EncryptedPassword =
                    _settingsService.EncryptPassword(
                        PasswordBox.Password);
            }
            else
            {
                settings.EncryptedPassword = "";
            }

            _settingsService.Save(settings);

            StatusText.Text = "Status: Settings saved.";

            DialogResult = true;

            Close();
        }


        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}