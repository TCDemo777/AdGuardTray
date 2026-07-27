using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AdGuardTray.Models;
using AdGuardTray.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdGuardTray.ViewModels
{
    public partial class ClientsViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly List<ClientInfo> _allClients = new();
        private RouterManager? _routerManager;

        public ObservableCollection<ClientInfo> Clients { get; } = new();

        public IReadOnlyList<string> SortOptions { get; } =
            new[]
            {
                "IP address",
                "Blocked queries",
                "Last seen",
                "Total queries",
                "Block rate",
                "Name"
            };

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private string selectedSortOption = "IP address";

        [ObservableProperty]
        private bool sortDescending;

        [ObservableProperty]
        private ClientInfo? selectedClient;

        [ObservableProperty]
        private string statusMessage = "No client data loaded.";

        [ObservableProperty]
        private bool isLoading;

        public string SortDirectionText =>
            SortDescending ? "Descending" : "Ascending";

        public ClientsViewModel()
        {
            _settingsService = new SettingsService();
        }

        [RelayCommand]
        public async Task LoadClientsAsync()
        {
            if (IsLoading)
            {
                return;
            }

            IsLoading = true;
            StatusMessage = "Loading clients...";

            try
            {
                if (_routerManager is null)
                {
                    var settings = _settingsService.Load();

                    if (string.IsNullOrWhiteSpace(settings.RouterIp) ||
                        string.IsNullOrWhiteSpace(settings.Username))
                    {
                        StatusMessage = "Router settings are incomplete.";
                        return;
                    }

                    string password =
                        _settingsService.DecryptPassword(
                            settings.EncryptedPassword);

                    if (string.IsNullOrWhiteSpace(password))
                    {
                        StatusMessage = "The router password is missing.";
                        return;
                    }

                    _routerManager = new RouterManager(
                        settings.RouterIp,
                        settings.Username,
                        password);
                }

                List<ClientInfo> clients =
                    await _routerManager.GetAdGuardClientsAsync();

                _allClients.Clear();
                _allClients.AddRange(clients);

                ApplyFilterAndSort();

                StatusMessage = _allClients.Count switch
                {
                    0 => "No clients were returned by AdGuard Home.",
                    1 => "1 client loaded.",
                    _ => $"{_allClients.Count} clients loaded."
                };
            }
            catch (Exception ex)
            {
                StatusMessage = "Unable to load clients: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void ToggleSortDirection()
        {
            SortDescending = !SortDescending;
            OnPropertyChanged(nameof(SortDirectionText));
            ApplyFilterAndSort();
        }

        partial void OnSearchTextChanged(string value)
        {
            ApplyFilterAndSort();
        }

        partial void OnSelectedSortOptionChanged(string value)
        {
            ApplyFilterAndSort();
        }

        partial void OnSortDescendingChanged(bool value)
        {
            OnPropertyChanged(nameof(SortDirectionText));
        }

        public void RefreshSort()
        {
            ApplyFilterAndSort();
        }

        private void ApplyFilterAndSort()
        {
            string search = SearchText.Trim();

            IEnumerable<ClientInfo> query = _allClients;

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(client =>
                    Contains(client.Name, search) ||
                    Contains(client.IpAddress, search) ||
                    Contains(client.MacAddress, search));
            }

            query = SelectedSortOption switch
            {
                "Blocked queries" => SortDescending
                    ? query.OrderByDescending(x => x.BlockedQueries)
                           .ThenBy(x => IpSortKey(x.IpAddress))
                    : query.OrderBy(x => x.BlockedQueries)
                           .ThenBy(x => IpSortKey(x.IpAddress)),

                "Last seen" => SortDescending
                    ? query.OrderByDescending(x => LastSeenSortKey(x.LastSeen))
                           .ThenBy(x => IpSortKey(x.IpAddress))
                    : query.OrderBy(x => LastSeenSortKey(x.LastSeen))
                           .ThenBy(x => IpSortKey(x.IpAddress)),

                "Total queries" => SortDescending
                    ? query.OrderByDescending(x => x.TotalQueries)
                           .ThenBy(x => IpSortKey(x.IpAddress))
                    : query.OrderBy(x => x.TotalQueries)
                           .ThenBy(x => IpSortKey(x.IpAddress)),

                "Block rate" => SortDescending
                    ? query.OrderByDescending(x => x.BlockRate)
                           .ThenBy(x => IpSortKey(x.IpAddress))
                    : query.OrderBy(x => x.BlockRate)
                           .ThenBy(x => IpSortKey(x.IpAddress)),

                "Name" => SortDescending
                    ? query.OrderByDescending(x => x.Name, StringComparer.OrdinalIgnoreCase)
                           .ThenBy(x => IpSortKey(x.IpAddress))
                    : query.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                           .ThenBy(x => IpSortKey(x.IpAddress)),

                _ => SortDescending
                    ? query.OrderByDescending(x => IpSortKey(x.IpAddress))
                    : query.OrderBy(x => IpSortKey(x.IpAddress))
            };

            Clients.Clear();

            foreach (ClientInfo client in query)
            {
                Clients.Add(client);
            }

            if (!IsLoading && _allClients.Count > 0)
            {
                StatusMessage =
                    $"{Clients.Count} of {_allClients.Count} clients shown · " +
                    $"sorted by {SelectedSortOption.ToLowerInvariant()} " +
                    $"({SortDirectionText.ToLowerInvariant()}).";
            }
        }

        private static bool Contains(string? value, string search) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(search, StringComparison.OrdinalIgnoreCase);

        private static long IpSortKey(string? value)
        {
            if (!System.Net.IPAddress.TryParse(value, out var address))
            {
                return long.MaxValue;
            }

            byte[] bytes = address.GetAddressBytes();

            if (bytes.Length != 4)
            {
                return long.MaxValue - 1;
            }

            return ((long)bytes[0] << 24) |
                   ((long)bytes[1] << 16) |
                   ((long)bytes[2] << 8) |
                   bytes[3];
        }

        private static DateTime LastSeenSortKey(string? value)
        {
            return DateTime.TryParse(value, out DateTime parsed)
                ? parsed
                : DateTime.MinValue;
        }
    }
}
