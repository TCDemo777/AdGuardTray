using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Diagnostics;
using System.Windows;

namespace AdGuardTray
{
    public partial class MainWindow : Window
    {
        private TaskbarIcon trayIcon;
        private Views.DashboardWindow? dashboard;

        public MainWindow()
        {
            InitializeComponent();

            // Hide the host window
            Hide();

            // Create and show the dashboard
            dashboard = new Views.DashboardWindow();
            dashboard.Show();

            // Create tray icon
            trayIcon = new TaskbarIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                ToolTipText = "GL.iNet Desktop Manager",
                ContextMenu = new System.Windows.Controls.ContextMenu()
            };

            //
            // Dashboard
            //
            var dashboardItem = new System.Windows.Controls.MenuItem
            {
                Header = "Dashboard"
            };

            dashboardItem.Click += (s, e) =>
            {
                if (dashboard == null)
                {
                    dashboard = new Views.DashboardWindow();
                }

                dashboard.Show();
                dashboard.WindowState = WindowState.Normal;
                dashboard.Activate();
            };

            //
            // Settings
            //
            var settingsItem = new System.Windows.Controls.MenuItem
            {
                Header = "Settings"
            };

            settingsItem.Click += (s, e) =>
            {
                var settings = new Views.SettingsWindow();
                settings.ShowDialog();
            };

            //
            // Open AdGuard Home
            //
            var openAdGuard = new System.Windows.Controls.MenuItem
            {
                Header = "Open AdGuard Home"
            };

            openAdGuard.Click += OpenAdGuard_Click;

            //
            // Open Router
            //
            var openRouter = new System.Windows.Controls.MenuItem
            {
                Header = "Open GL.iNet Router"
            };

            openRouter.Click += (s, e) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "http://192.168.1.1/",
                    UseShellExecute = true
                });
            };

            //
            // Exit
            //
            var exit = new System.Windows.Controls.MenuItem
            {
                Header = "Exit"
            };

            exit.Click += (s, e) =>
            {
                trayIcon.Dispose();

                dashboard?.Close();

                Application.Current.Shutdown();
            };

            trayIcon.ContextMenu.Items.Add(dashboardItem);
            trayIcon.ContextMenu.Items.Add(settingsItem);
            trayIcon.ContextMenu.Items.Add(new System.Windows.Controls.Separator());

            trayIcon.ContextMenu.Items.Add(openAdGuard);
            trayIcon.ContextMenu.Items.Add(openRouter);

            trayIcon.ContextMenu.Items.Add(new System.Windows.Controls.Separator());

            trayIcon.ContextMenu.Items.Add(exit);

            // Double-click tray icon opens dashboard
            trayIcon.TrayMouseDoubleClick += (s, e) =>
            {
                if (dashboard == null)
                {
                    dashboard = new Views.DashboardWindow();
                }

                dashboard.Show();
                dashboard.WindowState = WindowState.Normal;
                dashboard.Activate();
            };
        }

        private async void OpenAdGuard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var router = new Services.RouterService();
                await router.OpenCorrectPageAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "AdGuardTray",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public void DisposeTrayIcon()
        {
            trayIcon?.Dispose();
        }

        protected override void OnClosed(EventArgs e)
        {
            trayIcon?.Dispose();
            base.OnClosed(e);
        }
    }
}