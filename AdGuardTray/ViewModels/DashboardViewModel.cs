using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AdGuardTray.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

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

        [ObservableProperty]
        private double storagePercentage;

        [ObservableProperty]
        private string storageUsed = "-";

        [ObservableProperty]
        private string storageAvailable = "-";

        [ObservableProperty]
        private string storageTotal = "-";

        public string CpuHealthText =>
            CpuPercentage >= 90
                ? "High load"
                : CpuPercentage >= 70
                    ? "Elevated"
                    : CpuPercentage > 0
                        ? "Healthy"
                        : "Unavailable";

        public string CpuHealthColour =>
            CpuPercentage >= 90
                ? "#C62828"
                : CpuPercentage >= 70
                    ? "#B26A00"
                    : CpuPercentage > 0
                        ? "#16803C"
                        : "#687386";

        public string MemoryHealthText =>
            MemoryPercentage >= 90
                ? "High usage"
                : MemoryPercentage >= 75
                    ? "Elevated"
                    : MemoryPercentage > 0
                        ? "Healthy"
                        : "Unavailable";

        public string MemoryHealthColour =>
            MemoryPercentage >= 90
                ? "#C62828"
                : MemoryPercentage >= 75
                    ? "#B26A00"
                    : MemoryPercentage > 0
                        ? "#16803C"
                        : "#687386";

        public string StorageHealthText =>
            StoragePercentage >= 90
                ? "Nearly full"
                : StoragePercentage >= 75
                    ? "Elevated"
                    : StoragePercentage > 0
                        ? "Healthy"
                        : "Unavailable";

        public string StorageHealthColour =>
            StoragePercentage >= 90
                ? "#C62828"
                : StoragePercentage >= 75
                    ? "#B26A00"
                    : StoragePercentage > 0
                        ? "#16803C"
                        : "#687386";


        //
        // AdGuard summary
        //

        [ObservableProperty]
        private bool adGuardRunning;

        [ObservableProperty]
        private bool adGuardProtectionEnabled;

        [ObservableProperty]
        private bool adGuardProtectionPaused;

        [ObservableProperty]
        private bool adGuardProtectionStatusKnown;

        [ObservableProperty]
        private string adGuardProtectionRemaining = "";

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
        {
            get;
        } = new();

        public ISeries[] QueryHistorySeries
        {
            get;
        } =
        {
            new LineSeries<double>()
        };

        public ObservableCollection<AdGuardRankedItem>
            TopClients
        {
            get;
        } = new();

        public ObservableCollection<AdGuardRankedItem>
            TopQueriedDomains
        {
            get;
        } = new();

        public ObservableCollection<AdGuardRankedItem>
            TopBlockedDomains
        {
            get;
        } = new();


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
                ? "Connected"
                : "Disconnected";

        public string RouterStatusColour =>
            RouterConnected
                ? "#16803C"
                : "#C62828";

        public string AdGuardStatusText =>
            AdGuardRunning
                ? "Running"
                : "Stopped";

        public string AdGuardStatusColour =>
            AdGuardRunning
                ? "#16803C"
                : "#C62828";

        public string AdGuardProtectionStatusText =>
            !AdGuardProtectionStatusKnown
                ? "Status unavailable"
                : AdGuardProtectionEnabled
                    ? "Protection enabled"
                    : AdGuardProtectionPaused
                        ? "Protection paused"
                        : "Protection disabled";

        public string AdGuardProtectionStatusColour =>
            !AdGuardProtectionStatusKnown
                ? "#687386"
                : AdGuardProtectionEnabled
                    ? "#16803C"
                    : AdGuardProtectionPaused
                        ? "#B26A00"
                        : "#C62828";

        public string InternetStatusText =>
            InternetConnected
                ? "Connected"
                : "Disconnected";

        public string InternetStatusColour =>
            InternetConnected
                ? "#16803C"
                : "#C62828";

        public string OverallStatusColour =>
            RouterConnected &&
            InternetConnected &&
            AdGuardRunning
                ? "#16803C"
                : "#C62828";


        //
        // Collection updates
        //

        public void UpdateAdGuardStatistics(
            AdGuardStatistics statistics)
        {
            ReplaceCollection(
                AdGuardQueryHistory,
                statistics.QueryHistory);

            ((LineSeries<double>)QueryHistorySeries[0]).Values =
                statistics.QueryHistory
                    .Select(point => (double)point.Queries)
                    .ToArray();

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

        public void UpdateRankingsFromQueryLog(
            IEnumerable<QueryLogEntry> entries,
            bool onlyWhenEmpty = true)
        {
            List<QueryLogEntry> snapshot =
                entries?.ToList() ??
                new List<QueryLogEntry>();

            if (snapshot.Count == 0)
            {
                return;
            }

            if (!onlyWhenEmpty ||
                TopClients.Count == 0)
            {
                ReplaceCollection(
                    TopClients,
                    snapshot
                        .Where(entry =>
                            !string.IsNullOrWhiteSpace(entry.Client))
                        .GroupBy(
                            entry => entry.Client,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group =>
                            new
                            {
                                Name = group.Key,
                                Count = group.Count()
                            })
                        .OrderByDescending(item => item.Count)
                        .ThenBy(item => item.Name)
                        .Take(10)
                        .Select(item =>
                            CreateRankedItem(
                                item.Name,
                                item.Count)));
            }

            if (!onlyWhenEmpty ||
                TopQueriedDomains.Count == 0)
            {
                ReplaceCollection(
                    TopQueriedDomains,
                    snapshot
                        .Where(entry =>
                            !string.IsNullOrWhiteSpace(entry.Domain))
                        .GroupBy(
                            entry => entry.Domain,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group =>
                            new
                            {
                                Name = group.Key,
                                Count = group.Count()
                            })
                        .OrderByDescending(item => item.Count)
                        .ThenBy(item => item.Name)
                        .Take(10)
                        .Select(item =>
                            CreateRankedItem(
                                item.Name,
                                item.Count)));
            }

            if (!onlyWhenEmpty ||
                TopBlockedDomains.Count == 0)
            {
                ReplaceCollection(
                    TopBlockedDomains,
                    snapshot
                        .Where(entry =>
                            entry.IsBlocked &&
                            !string.IsNullOrWhiteSpace(entry.Domain))
                        .GroupBy(
                            entry => entry.Domain,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group =>
                            new
                            {
                                Name = group.Key,
                                Count = group.Count()
                            })
                        .OrderByDescending(item => item.Count)
                        .ThenBy(item => item.Name)
                        .Take(10)
                        .Select(item =>
                            CreateRankedItem(
                                item.Name,
                                item.Count)));
            }
        }

        private static AdGuardRankedItem CreateRankedItem(
            string name,
            int count)
        {
            var item =
                new AdGuardRankedItem();

            // AdGuardTray has used different display-property names for this
            // model during development. Set whichever one exists without
            // introducing another compile-time dependency.
            Type itemType =
                typeof(AdGuardRankedItem);

            string[] namePropertyCandidates =
            {
                "Name",
                "Domain",
                "Label",
                "Value",
                "Client"
            };

            foreach (string propertyName in namePropertyCandidates)
            {
                var property =
                    itemType.GetProperty(propertyName);

                if (property?.CanWrite == true &&
                    property.PropertyType == typeof(string))
                {
                    property.SetValue(item, name);
                    break;
                }
            }

            string[] countPropertyCandidates =
            {
                "Count",
                "Queries",
                "Total",
                "ValueCount"
            };

            foreach (string propertyName in countPropertyCandidates)
            {
                var property =
                    itemType.GetProperty(propertyName);

                if (property?.CanWrite != true)
                {
                    continue;
                }

                if (property.PropertyType == typeof(int))
                {
                    property.SetValue(item, count);
                    break;
                }

                if (property.PropertyType == typeof(long))
                {
                    property.SetValue(item, (long)count);
                    break;
                }
            }

            return item;
        }

        public void ClearAdGuardStatistics()
        {
            AdGuardQueryHistory.Clear();
            TopClients.Clear();
            TopQueriedDomains.Clear();
            TopBlockedDomains.Clear();

            ((LineSeries<double>)QueryHistorySeries[0]).Values =
                Array.Empty<double>();

            AdGuardProtectionEnabled = false;
            AdGuardProtectionPaused = false;
            AdGuardProtectionStatusKnown = false;
            AdGuardProtectionRemaining = "";
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
            OnPropertyChanged(nameof(RouterStatusText));
            OnPropertyChanged(nameof(RouterStatusColour));
            OnPropertyChanged(nameof(AdGuardStatusText));
            OnPropertyChanged(nameof(AdGuardStatusColour));
            OnPropertyChanged(nameof(AdGuardProtectionStatusText));
            OnPropertyChanged(nameof(AdGuardProtectionStatusColour));
            OnPropertyChanged(nameof(InternetStatusText));
            OnPropertyChanged(nameof(InternetStatusColour));
            OnPropertyChanged(nameof(OverallStatusColour));
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
            RefreshStatusIndicators();
        }

        partial void OnAdGuardRunningChanged(
            bool value)
        {
            RefreshStatusIndicators();
        }

        partial void OnAdGuardProtectionEnabledChanged(
            bool value)
        {
            RefreshStatusIndicators();
        }

        partial void OnInternetConnectedChanged(
            bool value)
        {
            RefreshStatusIndicators();
        }

        partial void OnAdGuardProtectionPausedChanged(
            bool value)
        {
            RefreshStatusIndicators();
        }

        partial void OnAdGuardProtectionStatusKnownChanged(
            bool value)
        {
            RefreshStatusIndicators();
        }

        public void UpdateStorageUsage(string? rawStorage)
        {
            StorageUsage = string.IsNullOrWhiteSpace(rawStorage)
                ? "-"
                : rawStorage.Trim();

            StoragePercentage = 0;
            StorageUsed = "-";
            StorageAvailable = "-";
            StorageTotal = "-";

            if (string.IsNullOrWhiteSpace(rawStorage))
            {
                NotifyResourceHealthChanged();
                return;
            }

            string[] lines = rawStorage
                .Replace("\r", string.Empty)
                .Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);

            string? candidate = lines
                .FirstOrDefault(line =>
                    line.Contains("/overlay", StringComparison.OrdinalIgnoreCase) ||
                    line.EndsWith(" /", StringComparison.OrdinalIgnoreCase))
                ?? lines.LastOrDefault();

            if (string.IsNullOrWhiteSpace(candidate))
            {
                NotifyResourceHealthChanged();
                return;
            }

            string[] parts = candidate.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            int percentIndex = Array.FindIndex(
                parts,
                part => part.EndsWith("%", StringComparison.Ordinal));

            if (percentIndex >= 0 &&
                double.TryParse(
                    parts[percentIndex].TrimEnd('%'),
                    out double percent))
            {
                StoragePercentage = Math.Clamp(percent, 0, 100);

                // Typical df output:
                // Filesystem 1K-blocks Used Available Use% Mounted-on
                if (percentIndex >= 3)
                {
                    StorageTotal = FormatStorageSize(parts[percentIndex - 3]);
                    StorageUsed = FormatStorageSize(parts[percentIndex - 2]);
                    StorageAvailable = FormatStorageSize(parts[percentIndex - 1]);
                }

                StorageUsage = $"{StoragePercentage:0.#}% used";
            }

            NotifyResourceHealthChanged();
        }

        private static string FormatStorageSize(string value)
        {
            if (!double.TryParse(value, out double number))
            {
                return value;
            }

            // df commonly reports 1K blocks.
            double bytes = number * 1024d;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;

            while (bytes >= 1024d && unit < units.Length - 1)
            {
                bytes /= 1024d;
                unit++;
            }

            return $"{bytes:0.#} {units[unit]}";
        }

        private void NotifyResourceHealthChanged()
        {
            OnPropertyChanged(nameof(CpuHealthText));
            OnPropertyChanged(nameof(CpuHealthColour));
            OnPropertyChanged(nameof(MemoryHealthText));
            OnPropertyChanged(nameof(MemoryHealthColour));
            OnPropertyChanged(nameof(StorageHealthText));
            OnPropertyChanged(nameof(StorageHealthColour));
        }

        partial void OnCpuPercentageChanged(double value)
        {
            NotifyResourceHealthChanged();
        }

        partial void OnMemoryPercentageChanged(double value)
        {
            NotifyResourceHealthChanged();
        }

        partial void OnStoragePercentageChanged(double value)
        {
            NotifyResourceHealthChanged();
        }

    }
}