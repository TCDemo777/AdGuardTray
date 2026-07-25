using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AdGuardTray.Models;
using System.Linq;

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
        // AdGuard summary
        //

        [ObservableProperty]
        private bool adGuardRunning;

        [ObservableProperty]
        private bool adGuardProtectionEnabled;

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
        // AdGuard graph and rankings
        //

        [ObservableProperty]
        private double adGuardQueryGraphMaximum = 1;

        public ObservableCollection<AdGuardTimePoint>
            AdGuardQueryHistory
        { get; } =
                new();

        public ObservableCollection<AdGuardRankedItem>
            TopClients
        { get; } =
                new();

        public ObservableCollection<AdGuardRankedItem>
            TopQueriedDomains
        { get; } =
                new();

        public ObservableCollection<AdGuardRankedItem>
            TopBlockedDomains
        { get; } =
                new();

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

        public string AdGuardProtectionStatusText =>
            AdGuardProtectionEnabled
                ? "🟢 Protection enabled"
                : "🔴 Protection disabled";

        public string InternetStatusText =>
            InternetConnected
                ? "🟢 Connected"
                : "🔴 Offline";


        //
        // Collection updates
        //

        public void UpdateAdGuardStatistics(
    AdGuardStatistics statistics)
        {
            AdGuardProtectionEnabled =
                statistics.ProtectionEnabled;

            ReplaceCollection(
                AdGuardQueryHistory,
                statistics.QueryHistory);

            AdGuardQueryGraphMaximum =
                statistics.QueryHistory.Count == 0
                    ? 1
                    : Math.Max(
                        1,
                        statistics.QueryHistory.Max(
                            point => point.Queries));

            ReplaceCollection(
                TopClients,
                statistics.TopClients);

            ReplaceCollection(
                TopQueriedDomains,
                statistics.TopQueriedDomains);

            ReplaceCollection(
                TopBlockedDomains,
                statistics.TopBlockedDomains);
        }

        public void ClearAdGuardStatistics()
        {
            AdGuardQueryHistory.Clear();
            TopClients.Clear();
            TopQueriedDomains.Clear();
            TopBlockedDomains.Clear();

            AdGuardProtectionEnabled = false;
            AdGuardQueryGraphMaximum = 1;
        }

        private static void ReplaceCollection<T>(
            ObservableCollection<T> destination,
            IEnumerable<T> source)
        {
            destination.Clear();

            foreach (T item in source)
            {
                destination.Add(item);
            }
        }


        //
        // Refresh indicators
        //

        public void RefreshStatusIndicators()
        {
            OnPropertyChanged(
                nameof(RouterStatusText));

            OnPropertyChanged(
                nameof(AdGuardStatusText));

            OnPropertyChanged(
                nameof(AdGuardProtectionStatusText));

            OnPropertyChanged(
                nameof(InternetStatusText));
        }


        //
        // Convert CPU text to progress value
        //

        partial void OnCpuUsageChanged(
            string value)
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

        partial void OnMemoryUsageChanged(
            string value)
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

        partial void OnRouterConnectedChanged(
            bool value)
        {
            OnPropertyChanged(
                nameof(RouterStatusText));
        }

        partial void OnAdGuardRunningChanged(
            bool value)
        {
            OnPropertyChanged(
                nameof(AdGuardStatusText));
        }

        partial void OnAdGuardProtectionEnabledChanged(
            bool value)
        {
            OnPropertyChanged(
                nameof(AdGuardProtectionStatusText));
        }

        partial void OnInternetConnectedChanged(
            bool value)
        {
            OnPropertyChanged(
                nameof(InternetStatusText));
        }
    }


}
