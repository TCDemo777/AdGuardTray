using System;
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

            _settingsService =
                new SettingsService();

            LoadSettings();

            SaveButton.Click += SaveButton_Click;
        }

        private void LoadSettings()
        {
            AppSettings settings =
                _settingsService.Load();

            RouterIpBox.Text =
                settings.RouterIp;

            UsernameBox.Text =
                settings.Username;

            PasswordBox.Password =
                _settingsService.DecryptPassword(
                    settings.EncryptedPassword);

            RememberPasswordCheck.IsChecked =
                settings.RememberPassword;

            StartWithWindowsCheck.IsChecked =
                settings.StartWithWindows;

            StatusText.Text =
                "Status: Ready";
        }

        private async void TestAdGuardButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            StatusText.Text =
                "Status: Testing AdGuard...";

            bool connected =
                await App.AdGuard.IsAvailableAsync(
                    "http://192.168.1.1:3000/");

            StatusText.Text =
                connected
                ? "Status: AdGuard Home reachable."
                : "Status: Cannot reach AdGuard Home.";
        }

        private async void GetStatusButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                StatusText.Text =
                    "Status: Connecting via SSH...";

                var ssh =
                    new GLInetSshService(
                        RouterIpBox.Text.Trim(),
                        UsernameBox.Text.Trim(),
                        PasswordBox.Password);

                StatusText.Text =
                    "Status: Checking AdGuard Home...";

                string serviceStatus =
                    await ssh.RunCommandAsync(
                        "/etc/init.d/adguardhome status");

                string processInfo =
                    await ssh.RunCommandAsync(
                        "pgrep -a AdGuardHome");

                string version =
                    await ssh.RunCommandAsync(
                        "/usr/bin/AdGuardHome --version");

                string output =
                    "GL.iNet AdGuard Home Status\r\n" +
                    "===========================\r\n\r\n" +

                    "Router\r\n" +
                    "------\r\n" +
                    RouterIpBox.Text.Trim() +

                    "\r\n\r\nUsername\r\n" +
                    "--------\r\n" +
                    UsernameBox.Text.Trim() +

                    "\r\n\r\nService Status\r\n" +
                    "--------------\r\n" +
                    serviceStatus.Trim() +

                    "\r\n\r\nRunning Process\r\n" +
                    "---------------\r\n" +
                    (string.IsNullOrWhiteSpace(processInfo)
                        ? "Not Running"
                        : processInfo.Trim()) +

                    "\r\n\r\nVersion\r\n" +
                    "-------\r\n" +
                    version.Trim();

                ShowOutput(
                    "AdGuard Home Status",
                    output);

                StatusText.Text =
                    "Status: Complete.";
            }
            catch (Exception ex)
            {
                StatusText.Text =
                    "Status: SSH failed.";

                ShowOutput(
                    "SSH Error",
                    ex.ToString());
            }
        }

        private void ShowOutput(
            string title,
            string text)
        {
            var outputWindow =
                new Window
                {
                    Title = title,
                    Width = 800,
                    Height = 500,
                    WindowStartupLocation =
                        WindowStartupLocation.CenterScreen
                };

            var textBox =
                new System.Windows.Controls.TextBox
                {
                    Text = text,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    AcceptsTab = true,
                    TextWrapping =
                        System.Windows.TextWrapping.NoWrap,
                    VerticalScrollBarVisibility =
                        System.Windows.Controls.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility =
                        System.Windows.Controls.ScrollBarVisibility.Auto
                };

            outputWindow.Content =
                textBox;

            outputWindow.ShowDialog();
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var settings =
                new AppSettings
                {
                    RouterIp =
                        RouterIpBox.Text.Trim(),

                    Username =
                        UsernameBox.Text.Trim(),

                    RememberPassword =
                        RememberPasswordCheck.IsChecked == true,

                    StartWithWindows =
                        StartWithWindowsCheck.IsChecked == true
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

            StatusText.Text =
                "Status: Settings saved.";

            DialogResult = true;

            Close();
        }

        private void Cancel_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}