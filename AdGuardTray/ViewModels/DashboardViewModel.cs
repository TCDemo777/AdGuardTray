using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdGuardTray.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RouterStatusText))]
        [NotifyPropertyChangedFor(nameof(RouterStatusBrush))]
        private bool routerConnected;

        [ObservableProperty]
        private string routerModel = "-";

        [ObservableProperty]
        private string firmwareVersion = "-";

        [ObservableProperty]
        private string uptime = "-";

        [ObservableProperty]
        private string memoryUsage = "-";

        [ObservableProperty]
        private string storageUsage = "-";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AdGuardStatusText))]
        [NotifyPropertyChangedFor(nameof(AdGuardStatusBrush))]
        private bool adGuardRunning;

        [ObservableProperty]
        private string adGuardVersion = "-";

        [ObservableProperty]
        private string adGuardProcess = "-";

        [ObservableProperty]
        private string adGuardService = "-";

        public string RouterStatusText =>
            RouterConnected ? "Connected" : "Offline";

        public Brush RouterStatusBrush =>
            RouterConnected ? Brushes.LimeGreen : Brushes.Red;

        public string AdGuardStatusText =>
            AdGuardRunning ? "Running" : "Stopped";

        public Brush AdGuardStatusBrush =>
            AdGuardRunning ? Brushes.LimeGreen : Brushes.Red;
    }
}