using Hardcodet.Wpf.TaskbarNotification;
using System;
using System.Diagnostics;
using System.Windows;

namespace AdGuardTray
{
    public partial class MainWindow : Window
    {
        private TaskbarIcon trayIcon;

        public MainWindow()
        {
            InitializeComponent();

            // Show Settings on first launch (temporary)
            new Views.SettingsWindow().ShowDialog();

            Hide();

            trayIcon = new TaskbarIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                ToolTipText = "AdGuard Tray",
                ContextMenu = new System.Windows.Controls.ContextMenu()
            };



            //
            // Dashboard
            //

            var dashboard =
                new System.Windows.Controls.MenuItem
                {
                    Header = "Dashboard"
                };

            dashboard.Click += (s, e) =>
            {
                var window =
                    new Views.DashboardWindow();

                window.Show();
                window.Activate();
            };



            //
            // Open AdGuard Home
            //

            var openAdGuard =
                new System.Windows.Controls.MenuItem
                {
                    Header = "Open AdGuard Home"
                };

            openAdGuard.Click += OpenAdGuard_Click;



            //
            // Open Router
            //

            var openRouter =
                new System.Windows.Controls.MenuItem
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
            // Settings
            //

            var settings =
                new System.Windows.Controls.MenuItem
                {
                    Header = "Settings"
                };

            settings.Click += (s, e) =>
            {
                new Views.SettingsWindow()
                    .ShowDialog();
            };



            //
            // Diagnostics
            //

            var diagnostics =
                new System.Windows.Controls.MenuItem
                {
                    Header = "Diagnostics"
                };

            diagnostics.Click += (s, e) =>
            {
                new Views.DiagnosticsWindow()
                    .Show();
            };



            //
            // Exit
            //

            var exit =
                new System.Windows.Controls.MenuItem
                {
                    Header = "Exit"
                };

            exit.Click += (s, e) =>
            {
                trayIcon.Dispose();
                Application.Current.Shutdown();
            };



            //
            // Build Menu
            //

            trayIcon.ContextMenu.Items.Add(dashboard);
            trayIcon.ContextMenu.Items.Add(settings);

            trayIcon.ContextMenu.Items.Add(
                new System.Windows.Controls.Separator());

            trayIcon.ContextMenu.Items.Add(openAdGuard);
            trayIcon.ContextMenu.Items.Add(openRouter);

            trayIcon.ContextMenu.Items.Add(
                new System.Windows.Controls.Separator());

            trayIcon.ContextMenu.Items.Add(diagnostics);

            trayIcon.ContextMenu.Items.Add(
                new System.Windows.Controls.Separator());

            trayIcon.ContextMenu.Items.Add(exit);
        }





        private async void OpenAdGuard_Click(
            object sender,
            RoutedEventArgs e)
        {
            var router =
                new Services.RouterService();

            await router.OpenCorrectPageAsync();
        }





        public void DisposeTrayIcon()
        {
            trayIcon?.Dispose();
        }





        protected override void OnClosed(
            EventArgs e)
        {
            trayIcon?.Dispose();

            base.OnClosed(e);
        }
    }
}