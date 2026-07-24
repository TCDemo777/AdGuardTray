using System;
using System.Windows;
using AdGuardTray.Services;

namespace AdGuardTray
{
    public partial class App : Application
    {
        private MainWindow? mainWindow;

        public static AdGuardService AdGuard { get; } = new AdGuardService();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            mainWindow = new MainWindow();

            // Start hidden in tray
            mainWindow.Hide();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            mainWindow?.DisposeTrayIcon();

            base.OnExit(e);
        }
    }
}