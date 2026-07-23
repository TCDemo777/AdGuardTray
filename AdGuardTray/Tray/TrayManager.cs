using Hardcodet.Wpf.TaskbarNotification;

namespace AdGuardTray.Tray;

public class TrayManager
{
    public TaskbarIcon Icon { get; }

    public TrayManager()
    {
        Icon = (TaskbarIcon)App.Current.FindResource("TrayIcon");
    }
}