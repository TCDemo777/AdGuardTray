using System;
using System.Windows;
using AdGuardTray.Models;
using AdGuardTray.Services;
using AdGuardTray.Views;

namespace AdGuardTray
{
    public partial class App : Application
    {
        protected override void OnStartup(
            StartupEventArgs e)
        {
            base.OnStartup(e);

            ShutdownMode =
                ShutdownMode.OnMainWindowClose;

            Window startupWindow =
                HasUsableSavedSettings()
                    ? new DashboardWindow()
                    : CreateFirstRunSettingsWindow();

            MainWindow = startupWindow;
            startupWindow.Show();
        }

        private static bool HasUsableSavedSettings()
        {
            try
            {
                var settingsService =
                    new SettingsService();

                AppSettings settings =
                    settingsService.Load();

                if (string.IsNullOrWhiteSpace(settings.RouterIp) ||
                    string.IsNullOrWhiteSpace(settings.Username))
                {
                    return false;
                }

                if (!settings.RememberPassword ||
                    string.IsNullOrWhiteSpace(settings.EncryptedPassword))
                {
                    return false;
                }

                string password =
                    settingsService.DecryptPassword(
                        settings.EncryptedPassword);

                return !string.IsNullOrWhiteSpace(password);
            }
            catch
            {
                return false;
            }
        }

        private static Window CreateFirstRunSettingsWindow()
        {
            return new Window
            {
                Title = "AdGuardTray Setup",
                Width = 920,
                Height = 700,
                MinWidth = 760,
                MinHeight = 560,
                WindowStartupLocation =
                    WindowStartupLocation.CenterScreen,
                Content = new SettingsView()
            };
        }
    }
}
