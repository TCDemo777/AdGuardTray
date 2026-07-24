using CommunityToolkit.Mvvm.ComponentModel;

namespace AdGuardTray.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool routerConnected;

        [ObservableProperty]
        private bool adGuardRunning;

        [ObservableProperty]
        private string routerModel = "Unknown";

        [ObservableProperty]
        private string firmwareVersion = "-";

        [ObservableProperty]
        private string uptime = "-";

        [ObservableProperty]
        private string adGuardVersion = "-";

        [ObservableProperty]
        private string memoryUsage = "-";

        [ObservableProperty]
        private string storageUsage = "-";
    }
}