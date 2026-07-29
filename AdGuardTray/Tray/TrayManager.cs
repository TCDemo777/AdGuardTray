using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;

namespace AdGuardTray.Tray
{
    public sealed class TrayManager : IDisposable
    {
        private readonly Action _openDashboard;
        private readonly Action _refreshDashboard;
        private readonly Action _exitApplication;

        public TaskbarIcon Icon { get; }

        public TrayManager(
            Action openDashboard,
            Action refreshDashboard,
            Action exitApplication)
        {
            _openDashboard = openDashboard;
            _refreshDashboard = refreshDashboard;
            _exitApplication = exitApplication;

            Icon = new TaskbarIcon
            {
                ToolTipText = "AdGuardTray",
                IconSource = BitmapFrame.Create(
                    new Uri(
                        "pack://application:,,,/Assets/AdGuardTray.ico",
                        UriKind.Absolute)),
                ContextMenu = BuildContextMenu()
            };

            Icon.TrayMouseDoubleClick +=
                (_, _) => _openDashboard();
        }

        public void ShowStillRunningMessage()
        {
            Icon.ShowBalloonTip(
                "AdGuardTray",
                "AdGuardTray is still running in the notification area.",
                BalloonIcon.Info);
        }

        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();

            var openItem = new MenuItem
            {
                Header = "Open Dashboard",
                FontWeight = FontWeights.SemiBold
            };
            openItem.Click += (_, _) => _openDashboard();

            var refreshItem = new MenuItem
            {
                Header = "Refresh Dashboard"
            };
            refreshItem.Click += (_, _) => _refreshDashboard();

            var exitItem = new MenuItem
            {
                Header = "Exit AdGuardTray"
            };
            exitItem.Click += (_, _) => _exitApplication();

            menu.Items.Add(openItem);
            menu.Items.Add(refreshItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(exitItem);

            return menu;
        }

        public void Dispose()
        {
            Icon.Dispose();
        }
    }
}
