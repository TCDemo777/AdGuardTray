using System;
using System.Windows;
using AdGuardTray.Models;
using AdGuardTray.Services;
using AdGuardTray.Tray;
using AdGuardTray.Views;

namespace AdGuardTray
{
    public partial class App : Application
    {
        private DashboardWindow? _dashboardWindow;
        private TrayManager? _trayManager;
        private bool _trayNoticeShown;

        public bool IsExitRequested { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (!HasUsableSavedSettings())
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                Window settingsWindow = CreateFirstRunSettingsWindow();
                MainWindow = settingsWindow;
                settingsWindow.Show();
                return;
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _dashboardWindow = new DashboardWindow();
            MainWindow = _dashboardWindow;

            _trayManager = new TrayManager(
                ShowDashboard,
                RefreshDashboard,
                ExitApplication);

            _dashboardWindow.Show();
        }

        public void HideDashboard()
        {
            if (_dashboardWindow is null || IsExitRequested)
            {
                return;
            }

            _dashboardWindow.Hide();

            if (!_trayNoticeShown)
            {
                _trayManager?.ShowStillRunningMessage();
                _trayNoticeShown = true;
            }
        }

        public void ShowDashboard()
        {
            if (_dashboardWindow is null)
            {
                return;
            }

            if (!_dashboardWindow.IsVisible)
            {
                _dashboardWindow.Show();
            }

            if (_dashboardWindow.WindowState == WindowState.Minimized)
            {
                _dashboardWindow.WindowState = WindowState.Normal;
            }

            _dashboardWindow.Activate();
            _dashboardWindow.Topmost = true;
            _dashboardWindow.Topmost = false;
            _dashboardWindow.Focus();
        }

        private async void RefreshDashboard()
        {
            ShowDashboard();

            if (_dashboardWindow is not null)
            {
                await _dashboardWindow.RefreshNowAsync();
            }
        }

        private void ExitApplication()
        {
            IsExitRequested = true;
            _trayManager?.Dispose();
            _trayManager = null;
            _dashboardWindow?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayManager?.Dispose();
            base.OnExit(e);
        }

        private static bool HasUsableSavedSettings()
        {
            try
            {
                var settingsService = new SettingsService();
                AppSettings settings = settingsService.Load();

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

                string password = settingsService.DecryptPassword(
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
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new SettingsView()
            };
        }
    }
}
