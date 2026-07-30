using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AdGuardTray.Models;
using AdGuardTray.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdGuardTray.ViewModels
{
    public partial class ClientDetailsViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly DispatcherTimer _refreshTimer;
        private readonly ClientInfo _client;

        private RouterManager? _routerManager;

        public ObservableCollection<QueryLogEntry> RecentQueries { get; } =
            new();

        public ObservableCollection<DomainStat> TopDomains { get; } =
            new();

        public ObservableCollection<DomainStat> TopBlockedDomains { get; } =
            new();

        public string ClientName => _client.Name;
        public string IpAddress => _client.IpAddress;
        public string MacAddress => _client.MacAddress;
        public string LastSeen => _client.LastSeen;
        public int TotalQueries => _client.TotalQueries;
        public int BlockedQueries => _client.BlockedQueries;
        public double BlockRate => _client.BlockRate;

        public bool HasRecentQueries => RecentQueries.Count > 0;
        public bool HasTopDomains => TopDomains.Count > 0;
        public bool HasTopBlockedDomains => TopBlockedDomains.Count > 0;

        [ObservableProperty]
        private string statusMessage =
            "Loading client activity...";

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private bool isPaused;

        public string PauseButtonText =>
            IsPaused ? "Resume" : "Pause";

        public ClientDetailsViewModel(
            ClientInfo client)
        {
            _client = client;
            _settingsService = new SettingsService();

            _refreshTimer =
                new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };

            _refreshTimer.Tick += RefreshTimer_Tick;
        }

        public async Task StartAsync()
        {
            if (_routerManager is null)
            {
                var settings =
                    _settingsService.Load();

                if (string.IsNullOrWhiteSpace(settings.RouterIp) ||
                    string.IsNullOrWhiteSpace(settings.Username))
                {
                    StatusMessage =
                        "Router settings are incomplete.";
                    return;
                }

                string password =
                    _settingsService.DecryptPassword(
                        settings.EncryptedPassword);

                if (string.IsNullOrWhiteSpace(password))
                {
                    StatusMessage =
                        "The router password is missing.";
                    return;
                }

                _routerManager =
                    new RouterManager(
                        settings.RouterIp,
                        settings.Username,
                        password);
            }

            await RefreshAsync();

            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }
        }

        public void Stop()
        {
            _refreshTimer.Stop();
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsLoading ||
                IsPaused ||
                _routerManager is null)
            {
                return;
            }

            IsLoading = true;
            StatusMessage =
                "Refreshing client activity...";

            try
            {
                List<QueryLogEntry> entries =
                    await _routerManager.GetQueryLogAsync();

                ApplyEntries(
                    entries
                        .Where(MatchesClient)
                        .ToList());
            }
            catch (Exception ex)
            {
                StatusMessage =
                    "Unable to load client activity: " +
                    ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void CopyIp()
        {
            CopyToClipboard(IpAddress, "IP address");
        }

        [RelayCommand]
        private void CopyMac()
        {
            CopyToClipboard(MacAddress, "MAC address");
        }

        private void CopyToClipboard(string? value, string label)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "-")
            {
                StatusMessage = $"No {label.ToLowerInvariant()} is available to copy.";
                return;
            }

            Clipboard.SetText(value);
            StatusMessage = $"{label} copied to the clipboard.";
        }

        [RelayCommand]
        private void TogglePause()
        {
            IsPaused = !IsPaused;

            OnPropertyChanged(
                nameof(PauseButtonText));

            StatusMessage =
                IsPaused
                    ? "Live updates paused."
                    : "Live updates resumed.";
        }

        private bool MatchesClient(
            QueryLogEntry entry)
        {
            return SameText(
                       entry.ClientAddress,
                       _client.IpAddress) ||
                   SameText(
                       entry.ClientName,
                       _client.Name) ||
                   SameText(
                       entry.Client,
                       _client.IpAddress) ||
                   SameText(
                       entry.Client,
                       _client.Name) ||
                   ContainsIdentifier(
                       entry.Client,
                       _client.IpAddress);
        }

        private void ApplyEntries(
            List<QueryLogEntry> entries)
        {
            RecentQueries.Clear();

            foreach (QueryLogEntry entry in entries.Take(200))
            {
                RecentQueries.Add(entry);
            }

            ReplaceStats(
                TopDomains,
                BuildDomainStats(
                    entries,
                    blockedOnly: false));

            ReplaceStats(
                TopBlockedDomains,
                BuildDomainStats(
                    entries,
                    blockedOnly: true));

            OnPropertyChanged(nameof(HasRecentQueries));
            OnPropertyChanged(nameof(HasTopDomains));
            OnPropertyChanged(nameof(HasTopBlockedDomains));

            StatusMessage =
                entries.Count switch
                {
                    0 =>
                        "No recent DNS activity found for this client.",
                    1 =>
                        "1 recent DNS request loaded.",
                    _ =>
                        $"{entries.Count} recent DNS requests loaded."
                };
        }

        private static IEnumerable<DomainStat> BuildDomainStats(
            IEnumerable<QueryLogEntry> entries,
            bool blockedOnly)
        {
            List<DomainStat> results = entries
                .Where(
                    entry =>
                        (!blockedOnly || entry.IsBlocked) &&
                        !string.IsNullOrWhiteSpace(entry.Domain) &&
                        entry.Domain != "-")
                .GroupBy(
                    entry => entry.Domain,
                    StringComparer.OrdinalIgnoreCase)
                .Select(
                    group =>
                        new DomainStat
                        {
                            Domain = group.Key,
                            Count = group.Count()
                        })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Domain)
                .Take(5)
                .ToList();

            int maximum = results.Count == 0 ? 1 : results.Max(item => item.Count);
            for (int index = 0; index < results.Count; index++)
            {
                DomainStat item = results[index];
                item.Rank = index + 1;
                item.Percentage = Math.Max(4d, item.Count * 100d / maximum);
            }

            return results;
        }

        private static void ReplaceStats(
            ObservableCollection<DomainStat> target,
            IEnumerable<DomainStat> source)
        {
            target.Clear();

            foreach (DomainStat item in source)
            {
                target.Add(item);
            }
        }

        private static bool SameText(
            string? first,
            string? second)
        {
            if (string.IsNullOrWhiteSpace(first) ||
                string.IsNullOrWhiteSpace(second) ||
                second == "-")
            {
                return false;
            }

            return string.Equals(
                first.Trim(),
                second.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }


        private static bool ContainsIdentifier(
            string? displayValue,
            string? identifier)
        {
            if (string.IsNullOrWhiteSpace(displayValue) ||
                string.IsNullOrWhiteSpace(identifier) ||
                identifier == "-")
            {
                return false;
            }

            return displayValue.Contains(
                identifier.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private async void RefreshTimer_Tick(
            object? sender,
            EventArgs e)
        {
            await RefreshAsync();
        }
    }
}
