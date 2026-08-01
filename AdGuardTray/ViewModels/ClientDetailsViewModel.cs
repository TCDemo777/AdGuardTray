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
        private readonly IRouterManagerProvider _routerManagerProvider;
        private readonly DeviceHistoryService _deviceHistoryService;
        private readonly HistoryRepository _historyRepository;
        private readonly ClientProfileService _clientProfileService;
        private readonly IntelligenceService _intelligenceService;
        private readonly Dictionary<string, ClientProfile> _clientProfiles;
        private readonly DispatcherTimer _refreshTimer;
        private readonly ClientInfo _client;

        public ObservableCollection<QueryLogEntry> RecentQueries { get; } =
            new();

        public ObservableCollection<DomainStat> TopDomains { get; } =
            new();

        public ObservableCollection<DomainStat> TopBlockedDomains { get; } =
            new();

        public ObservableCollection<DeviceConnectionEvent> RecentActivity { get; } =
            new();

        public string ClientName =>
            string.IsNullOrWhiteSpace(ProfileNickname)
                ? _client.Name
                : ProfileNickname;
        public string IpAddress => _client.IpAddress;
        public string MacAddress => _client.MacAddress;
        public string ClientLastSeen => _client.LastSeen;
        public int TotalQueries => _client.TotalQueries;
        public int BlockedQueries => _client.BlockedQueries;
        public double BlockRate => _client.BlockRate;

        public DateTimeOffset? FirstSeen { get; private set; }
        public DateTimeOffset? LastSeen { get; private set; }
        public long TimesConnected { get; private set; }
        public bool IsCurrentlyOnline { get; private set; }
        public IReadOnlyList<string> PreviousIpAddresses { get; private set; } =
            Array.Empty<string>();
        public IReadOnlyList<string> PreviousNetworkNames { get; private set; } =
            Array.Empty<string>();
        public bool HasHistory { get; private set; }
        public bool HasPreviousIpAddresses => PreviousIpAddresses.Count > 0;
        public bool HasPreviousNetworkNames => PreviousNetworkNames.Count > 0;
        public string HistoryStatus => IsCurrentlyOnline ? "Online" : "Offline";
        public string FirstSeenDisplay => FirstSeen?.ToLocalTime().ToString("g") ?? "—";
        public string LastSeenHistoryDisplay =>
            LastSeen?.ToLocalTime().ToString("g") ?? "—";

        public bool HasRecentQueries => RecentQueries.Count > 0;
        public bool HasTopDomains => TopDomains.Count > 0;
        public bool HasTopBlockedDomains => TopBlockedDomains.Count > 0;
        public bool HasRecentActivity => RecentActivity.Count > 0;
        public string TypicalOnlineTime { get; private set; } = "Not enough history";
        public string TypicalNetwork { get; private set; } = "Not enough history";
        public string AverageSessionLength { get; private set; } = "Not enough history";
        public int? DaysSinceLastSeen { get; private set; }
        public string DaysSinceLastSeenDisplay => DaysSinceLastSeen is { } days
            ? $"{days} day{(days == 1 ? "" : "s")}" : "Not enough history";

        [ObservableProperty]
        private string statusMessage =
            "Loading client activity...";

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private bool isPaused;

        [ObservableProperty]
        private string profileNickname = string.Empty;

        [ObservableProperty]
        private string profileCategory = string.Empty;

        [ObservableProperty]
        private string profileNotes = string.Empty;

        public string PauseButtonText =>
            IsPaused ? "Resume" : "Pause";

        public ClientDetailsViewModel(
            ClientInfo client,
            IRouterManagerProvider routerManagerProvider,
            DeviceHistoryService deviceHistoryService,
            HistoryRepository historyRepository,
            IntelligenceService intelligenceService)
        {
            _client = client;
            _routerManagerProvider = routerManagerProvider;
            _deviceHistoryService = deviceHistoryService;
            _historyRepository = historyRepository;
            _intelligenceService = intelligenceService;
            _clientProfileService = new ClientProfileService();
            _clientProfiles = _clientProfileService.Load();

            LoadProfile();
            LoadHistory();

            _refreshTimer =
                new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };

            _refreshTimer.Tick += RefreshTimer_Tick;
        }

        public async Task StartAsync()
        {
            await LoadBehaviourProfileAsync();
            await LoadRecentActivityAsync();
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
                IsPaused)
            {
                return;
            }

            IsLoading = true;
            StatusMessage =
                "Refreshing client activity...";

            try
            {
                RouterManager routerManager =
                    await _routerManagerProvider.GetRouterManagerAsync();
                List<QueryLogEntry> entries =
                    await routerManager.GetQueryLogAsync();

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
        private void SaveProfile()
        {
            string key = ClientKey(_client);
            if (!_clientProfiles.TryGetValue(key, out ClientProfile? profile))
            {
                profile = new ClientProfile
                {
                    Key = key,
                    FirstSeenUtc = _client.FirstSeenUtc == default
                        ? DateTime.UtcNow
                        : _client.FirstSeenUtc
                };
                _clientProfiles[key] = profile;
            }

            profile.Nickname = ProfileNickname.Trim();
            profile.Category = ProfileCategory.Trim();
            profile.Notes = ProfileNotes.Trim();
            profile.IsFavorite = _client.IsFavorite;
            profile.LastSeenUtc = DateTime.UtcNow;

            _clientProfileService.Save(_clientProfiles.Values);

            if (!string.IsNullOrWhiteSpace(profile.Nickname))
            {
                _client.Name = profile.Nickname;
            }

            _client.CustomCategory = profile.Category;
            _client.Notes = profile.Notes;

            OnPropertyChanged(nameof(ClientName));
            ClientRefreshNotifier.RequestRefresh();
            StatusMessage = $"Profile saved for {ClientName}.";
        }

        [RelayCommand]
        private void ClearProfile()
        {
            string key = ClientKey(_client);
            bool wasFavorite = _client.IsFavorite;

            _clientProfiles.Remove(key);
            if (wasFavorite)
            {
                _clientProfiles[key] = new ClientProfile
                {
                    Key = key,
                    IsFavorite = true,
                    FirstSeenUtc = _client.FirstSeenUtc == default
                        ? DateTime.UtcNow
                        : _client.FirstSeenUtc,
                    LastSeenUtc = DateTime.UtcNow
                };
            }

            ProfileNickname = string.Empty;
            ProfileCategory = string.Empty;
            ProfileNotes = string.Empty;
            _client.CustomCategory = string.Empty;
            _client.Notes = string.Empty;

            _clientProfileService.Save(_clientProfiles.Values);
            OnPropertyChanged(nameof(ClientName));
            ClientRefreshNotifier.RequestRefresh();
            StatusMessage = "Custom client profile cleared.";
        }

        private void LoadProfile()
        {
            string key = ClientKey(_client);
            if (!_clientProfiles.TryGetValue(key, out ClientProfile? profile))
            {
                return;
            }

            ProfileNickname = profile.Nickname;
            ProfileCategory = profile.Category;
            ProfileNotes = profile.Notes;
        }

        private async Task LoadBehaviourProfileAsync()
        {
            DeviceBehaviourProfile? profile = await _intelligenceService
                .GetDeviceProfileAsync(_client.MacAddress);
            if (profile is null) return;
            TypicalOnlineTime = profile.TypicalOnlineTimeDisplay;
            TypicalNetwork = profile.PreferredNetworkDisplay;
            AverageSessionLength = profile.AverageSessionDisplay;
            DaysSinceLastSeen = profile.DaysSinceLastSeen;
            OnPropertyChanged(nameof(TypicalOnlineTime));
            OnPropertyChanged(nameof(TypicalNetwork));
            OnPropertyChanged(nameof(AverageSessionLength));
            OnPropertyChanged(nameof(DaysSinceLastSeen));
            OnPropertyChanged(nameof(DaysSinceLastSeenDisplay));
        }

        private async Task LoadRecentActivityAsync()
        {
            IReadOnlyList<DeviceConnectionEvent> events;
            try
            {
                events = await _historyRepository.GetRecentEventsByMacAsync(
                    _client.MacAddress,
                    20);
            }
            catch
            {
                events = Array.Empty<DeviceConnectionEvent>();
            }

            RecentActivity.Clear();
            foreach (DeviceConnectionEvent connectionEvent in events)
                RecentActivity.Add(connectionEvent);

            OnPropertyChanged(nameof(HasRecentActivity));
        }

        private void LoadHistory()
        {
            DeviceHistoryRecord? history =
                _deviceHistoryService.GetByMacAddress(_client.MacAddress);
            if (history is null)
                return;

            HasHistory = true;
            FirstSeen = history.FirstSeen;
            LastSeen = history.LastSeen;
            TimesConnected = history.TimesConnected;
            IsCurrentlyOnline = history.IsCurrentlyOnline;

            PreviousIpAddresses = history.PreviousIpAddresses
                .Where(address =>
                    !string.IsNullOrWhiteSpace(address) &&
                    !address.Equals(
                        _client.IpAddress,
                        StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Reverse()
                .ToArray();

            PreviousNetworkNames = history.PreviousNetworkNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Reverse()
                .ToArray();
        }

        private static string ClientKey(ClientInfo client)
        {
            if (!string.IsNullOrWhiteSpace(client.MacAddress) &&
                client.MacAddress != "-")
            {
                return new string(
                    client.MacAddress
                        .Where(char.IsLetterOrDigit)
                        .Select(char.ToUpperInvariant)
                        .ToArray());
            }

            return client.IpAddress.Trim();
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
