using CommunityToolkit.Mvvm.ComponentModel;

namespace AdGuardTray.ViewModels
{
    public partial class DashboardViewModel : ObservableObject
    {
        //
        // Router
        //

        [ObservableProperty]
        private bool routerConnected;

        [ObservableProperty]
        private string routerModel = "-";

        [ObservableProperty]
        private string firmwareVersion = "-";

        [ObservableProperty]
        private string hostname = "-";

        [ObservableProperty]
        private string uptime = "-";

        [ObservableProperty]
        private string cpuUsage = "-";

        [ObservableProperty]
        private double cpuPercentage;


        [ObservableProperty]
        private string memoryUsage = "-";

        [ObservableProperty]
        private double memoryPercentage;


        [ObservableProperty]
        private string storageUsage = "-";



        //
        // AdGuard
        //

        [ObservableProperty]
        private bool adGuardRunning;

        [ObservableProperty]
        private string adGuardVersion = "-";

        [ObservableProperty]
        private string adGuardProcess = "-";

        [ObservableProperty]
        private string adGuardService = "-";

        [ObservableProperty]
        private string adGuardQueries = "-";

        [ObservableProperty]
        private string adGuardBlocked = "-";


        [ObservableProperty]
        private string adGuardBlockRate = "-";



        //
        // Internet
        //

        [ObservableProperty]
        private bool internetConnected;

        [ObservableProperty]
        private string wanIp = "-";

        [ObservableProperty]
        private string gateway = "-";

        [ObservableProperty]
        private string externalDns = "-";

        [ObservableProperty]
        private string advertisedDns = "-";

        [ObservableProperty]
        private string latency = "-";


        public string DnsServer
        {
            get => ExternalDns;
            set => ExternalDns = value;
        }



        //
        // Dashboard
        //

        [ObservableProperty]
        private string statusMessage = "Ready";

        [ObservableProperty]
        private string lastRefresh = "-";



        //
        // Status text
        //

        public string RouterStatusText =>
            RouterConnected
                ? "🟢 Connected"
                : "🔴 Offline";


        public string AdGuardStatusText =>
            AdGuardRunning
                ? "🟢 Running"
                : "🔴 Stopped";


        public string InternetStatusText =>
            InternetConnected
                ? "🟢 Connected"
                : "🔴 Offline";



        //
        // Refresh indicators
        //

        public void RefreshStatusIndicators()
        {
            OnPropertyChanged(nameof(RouterStatusText));
            OnPropertyChanged(nameof(AdGuardStatusText));
            OnPropertyChanged(nameof(InternetStatusText));
        }



        //
        // Convert CPU text to progress value
        //

        partial void OnCpuUsageChanged(string value)
        {
            if (double.TryParse(
                value.Replace("%", ""),
                out double result))
            {
                CpuPercentage = result;
            }
            else
            {
                CpuPercentage = 0;
            }
        }



        //
        // Convert memory text to progress value
        //

        partial void OnMemoryUsageChanged(string value)
        {
            if (double.TryParse(
                value.Replace("%", ""),
                out double result))
            {
                MemoryPercentage = result;
            }
            else
            {
                MemoryPercentage = 0;
            }
        }



        //
        // Status property updates
        //

        partial void OnRouterConnectedChanged(bool value)
        {
            OnPropertyChanged(nameof(RouterStatusText));
        }


        partial void OnAdGuardRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(AdGuardStatusText));
        }


        partial void OnInternetConnectedChanged(bool value)
        {
            OnPropertyChanged(nameof(InternetStatusText));
        }
    }
}