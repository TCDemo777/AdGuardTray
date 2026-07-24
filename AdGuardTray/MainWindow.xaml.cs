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

            // TEMPORARY - Show the Settings window on startup
            new Views.SettingsWindow().ShowDialog();

            Hide();

            trayIcon = new TaskbarIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                ToolTipText = "AdGuard Tray",
                ContextMenu = new System.Windows.Controls.ContextMenu()
            };

            var openAdGuard = new System.Windows.Controls.MenuItem
            {
                Header = "Open AdGuard Home"
            };

            openAdGuard.Click += OpenAdGuard_Click;

            var openRouter = new System.Windows.Controls.MenuItem
            {
                Header = "Open GL6000 Router"
            };

            openRouter.Click += (s, e) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "http://192.168.1.1/",
                    UseShellExecute = true
                });
            };

            var exit = new System.Windows.Controls.MenuItem
            {
                Header = "Exit"
            };

            exit.Click += (s, e) =>
            {
                trayIcon.Dispose();
                Application.Current.Shutdown();
            };

            trayIcon.ContextMenu.Items.Add(openAdGuard);
            trayIcon.ContextMenu.Items.Add(openRouter);
            trayIcon.ContextMenu.Items.Add(new System.Windows.Controls.Separator());
            trayIcon.ContextMenu.Items.Add(exit);
        }

        private async void OpenAdGuard_Click(object sender, RoutedEventArgs e)
        {
            var router = new Services.RouterService();
            await router.OpenCorrectPageAsync();
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