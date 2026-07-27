using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        private readonly HashSet<string> _favoriteKeys =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly string _favoritesFilePath;
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
                "Name",
                "Manufacturer",
                "Device type"
            };

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private string selectedSortOption = "IP address";

        [ObservableProperty]
        private bool sortDescending;

        [ObservableProperty]
        private bool showFavoritesOnly;

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

            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

            string folder = Path.Combine(
                appData,
                "AdGuardTray");

            Directory.CreateDirectory(folder);

            _favoritesFilePath =
                Path.Combine(folder, "client-favourites.json");

            LoadFavorites();
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

                foreach (ClientInfo client in clients)
                {
                    EnrichClient(client);
                }

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

        public void SelectSortOption(string? option)
        {
            if (string.IsNullOrWhiteSpace(option))
            {
                return;
            }

            // Assign and sort explicitly. This avoids relying on ComboBox
            // binding timing and makes selection changes immediate.
            selectedSortOption = option;
            OnPropertyChanged(nameof(SelectedSortOption));
            ApplyFilterAndSort();
        }

        public void ToggleFavorite(ClientInfo? client)
        {
            if (client is null)
            {
                return;
            }

            string key = ClientKey(client);

            if (_favoriteKeys.Contains(key))
            {
                _favoriteKeys.Remove(key);
                client.IsFavorite = false;
            }
            else
            {
                _favoriteKeys.Add(key);
                client.IsFavorite = true;
            }

            SaveFavorites();
            ApplyFilterAndSort();
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

        partial void OnShowFavoritesOnlyChanged(bool value)
        {
            ApplyFilterAndSort();
        }

        partial void OnSortDescendingChanged(bool value)
        {
            OnPropertyChanged(nameof(SortDirectionText));
        }

        private void ApplyFilterAndSort()
        {
            string search = SearchText.Trim();

            IEnumerable<ClientInfo> query = _allClients;

            if (ShowFavoritesOnly)
            {
                query = query.Where(client => client.IsFavorite);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(client =>
                    Contains(client.Name, search) ||
                    Contains(client.IpAddress, search) ||
                    Contains(client.MacAddress, search) ||
                    Contains(client.Manufacturer, search) ||
                    Contains(client.DeviceType, search) ||
                    Contains(client.HealthText, search));
            }

            query = SelectedSortOption switch
            {
                "Blocked queries" => SortDescending
                    ? query.OrderByDescending(x => x.BlockedQueries)
                    : query.OrderBy(x => x.BlockedQueries),

                "Last seen" => SortDescending
                    ? query.OrderByDescending(x => LastSeenSortKey(x.LastSeen))
                    : query.OrderBy(x => LastSeenSortKey(x.LastSeen)),

                "Total queries" => SortDescending
                    ? query.OrderByDescending(x => x.TotalQueries)
                    : query.OrderBy(x => x.TotalQueries),

                "Block rate" => SortDescending
                    ? query.OrderByDescending(x => x.BlockRate)
                    : query.OrderBy(x => x.BlockRate),

                "Name" => SortDescending
                    ? query.OrderByDescending(
                        x => x.Name,
                        StringComparer.OrdinalIgnoreCase)
                    : query.OrderBy(
                        x => x.Name,
                        StringComparer.OrdinalIgnoreCase),

                "Manufacturer" => SortDescending
                    ? query.OrderByDescending(
                        x => x.Manufacturer,
                        StringComparer.OrdinalIgnoreCase)
                    : query.OrderBy(
                        x => x.Manufacturer,
                        StringComparer.OrdinalIgnoreCase),

                "Device type" => SortDescending
                    ? query.OrderByDescending(
                        x => x.DeviceType,
                        StringComparer.OrdinalIgnoreCase)
                    : query.OrderBy(
                        x => x.DeviceType,
                        StringComparer.OrdinalIgnoreCase),

                _ => SortDescending
                    ? query.OrderByDescending(x => IpSortKey(x.IpAddress))
                    : query.OrderBy(x => IpSortKey(x.IpAddress))
            };

            // Favourites remain first without changing the selected sort.
            query = query
                .OrderByDescending(x => x.IsFavorite)
                .ThenBy(x => 0);

            // Reapply requested ordering inside favourite/non-favourite groups.
            query = ApplyGroupedSort(query);

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

        private IEnumerable<ClientInfo> ApplyGroupedSort(
            IEnumerable<ClientInfo> source)
        {
            IOrderedEnumerable<ClientInfo> grouped =
                source.OrderByDescending(x => x.IsFavorite);

            return SelectedSortOption switch
            {
                "Blocked queries" => SortDescending
                    ? grouped.ThenByDescending(x => x.BlockedQueries)
                    : grouped.ThenBy(x => x.BlockedQueries),

                "Last seen" => SortDescending
                    ? grouped.ThenByDescending(x => LastSeenSortKey(x.LastSeen))
                    : grouped.ThenBy(x => LastSeenSortKey(x.LastSeen)),

                "Total queries" => SortDescending
                    ? grouped.ThenByDescending(x => x.TotalQueries)
                    : grouped.ThenBy(x => x.TotalQueries),

                "Block rate" => SortDescending
                    ? grouped.ThenByDescending(x => x.BlockRate)
                    : grouped.ThenBy(x => x.BlockRate),

                "Name" => SortDescending
                    ? grouped.ThenByDescending(
                        x => x.Name,
                        StringComparer.OrdinalIgnoreCase)
                    : grouped.ThenBy(
                        x => x.Name,
                        StringComparer.OrdinalIgnoreCase),

                "Manufacturer" => SortDescending
                    ? grouped.ThenByDescending(
                        x => x.Manufacturer,
                        StringComparer.OrdinalIgnoreCase)
                    : grouped.ThenBy(
                        x => x.Manufacturer,
                        StringComparer.OrdinalIgnoreCase),

                "Device type" => SortDescending
                    ? grouped.ThenByDescending(
                        x => x.DeviceType,
                        StringComparer.OrdinalIgnoreCase)
                    : grouped.ThenBy(
                        x => x.DeviceType,
                        StringComparer.OrdinalIgnoreCase),

                _ => SortDescending
                    ? grouped.ThenByDescending(x => IpSortKey(x.IpAddress))
                    : grouped.ThenBy(x => IpSortKey(x.IpAddress))
            };
        }

        private void EnrichClient(ClientInfo client)
        {
            string combined =
                $"{client.Name} {client.Manufacturer}".ToLowerInvariant();

            (client.DeviceIcon, client.DeviceType) =
                DetectDevice(combined);

            client.Manufacturer =
                DetectManufacturer(client.MacAddress, client.Name);

            client.IsFavorite =
                _favoriteKeys.Contains(ClientKey(client));

            (client.HealthText, client.HealthColour) =
                DetectHealth(client);
        }

        private static (string Icon, string Type) DetectDevice(string value)
        {
            if (ContainsAny(value, "iphone", "ipad", "ios", "apple-mobile"))
            {
                return ("📱", "Apple mobile device");
            }

            if (ContainsAny(value, "android", "pixel", "galaxy", "phone"))
            {
                return ("📱", "Mobile device");
            }

            if (ContainsAny(value, "xbox", "playstation", "ps4", "ps5",
                "nintendo", "switch"))
            {
                return ("🎮", "Games console");
            }

            if (ContainsAny(value, "tv", "roku", "firestick", "chromecast",
                "appletv"))
            {
                return ("📺", "Media or smart TV");
            }

            if (ContainsAny(value, "printer", "epson", "brother", "laserjet"))
            {
                return ("▣", "Printer");
            }

            if (ContainsAny(value, "raspberry", "linux", "ubuntu", "debian",
                "server", "nas", "synology"))
            {
                return ("◆", "Server or Linux device");
            }

            if (ContainsAny(value, "laptop", "desktop", "windows", "pc",
                "macbook", "imac"))
            {
                return ("▰", "Computer");
            }

            return ("●", "Unknown device");
        }

        private static string DetectManufacturer(
            string? macAddress,
            string? name)
        {
            string prefix = NormalizeMac(macAddress);

            var oui = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["001A11"] = "Google",
                ["3C5A37"] = "Google",
                ["F4F5D8"] = "Google",
                ["001B63"] = "Apple",
                ["3C0754"] = "Apple",
                ["F0D1A9"] = "Apple",
                ["B827EB"] = "Raspberry Pi",
                ["DCA632"] = "Raspberry Pi",
                ["E45F01"] = "Raspberry Pi",
                ["001E10"] = "Shenzhen GL.iNet",
                ["94D9B3"] = "Shenzhen GL.iNet",
                ["9424E1"] = "Shenzhen GL.iNet",
                ["001A2B"] = "Cisco",
                ["001B44"] = "SanDisk",
                ["001C42"] = "Parallels",
                ["001D7E"] = "Cisco-Linksys",
                ["001E8C"] = "ASUSTek",
                ["001F3B"] = "Intel",
                ["0024E8"] = "Dell",
                ["0026B9"] = "Dell",
                ["001422"] = "Dell",
                ["00155D"] = "Microsoft",
                ["7C1E52"] = "Microsoft",
                ["0050F2"] = "Microsoft",
                ["001A79"] = "Samsung",
                ["0024E9"] = "Samsung",
                ["3C5AB4"] = "Google/Nest",
                ["AC84C6"] = "TP-Link",
                ["50C7BF"] = "TP-Link",
                ["00195B"] = "D-Link",
                ["001F33"] = "Netgear",
                ["000C29"] = "VMware",
                ["001C14"] = "VMware",
                ["080027"] = "Oracle VirtualBox"
            };

            if (prefix.Length >= 6 &&
                oui.TryGetValue(prefix[..6], out string? manufacturer))
            {
                return manufacturer;
            }

            string host = (name ?? string.Empty).ToLowerInvariant();

            if (host.Contains("iphone") ||
                host.Contains("ipad") ||
                host.Contains("macbook") ||
                host.Contains("imac"))
            {
                return "Apple";
            }

            if (host.Contains("galaxy") ||
                host.Contains("samsung"))
            {
                return "Samsung";
            }

            if (host.Contains("pixel") ||
                host.Contains("chromecast") ||
                host.Contains("google"))
            {
                return "Google";
            }

            if (host.Contains("raspberry"))
            {
                return "Raspberry Pi";
            }

            if (host.Contains("xbox"))
            {
                return "Microsoft";
            }

            if (host.Contains("playstation") ||
                host.Contains("ps5") ||
                host.Contains("ps4"))
            {
                return "Sony";
            }

            return "Unknown manufacturer";
        }

        private static (string Text, string Colour) DetectHealth(
            ClientInfo client)
        {
            if (DateTime.TryParse(
                client.LastSeen,
                out DateTime lastSeen))
            {
                TimeSpan age = DateTime.Now - lastSeen;

                if (age <= TimeSpan.FromMinutes(5))
                {
                    return ("Online", "#16803C");
                }

                if (age <= TimeSpan.FromHours(1))
                {
                    return ("Recently active", "#B26A00");
                }

                return ("Offline", "#687386");
            }

            if (client.TotalQueries > 0)
            {
                return ("Active", "#16803C");
            }

            return ("Unknown", "#687386");
        }

        private void LoadFavorites()
        {
            try
            {
                if (!File.Exists(_favoritesFilePath))
                {
                    return;
                }

                string json =
                    File.ReadAllText(_favoritesFilePath);

                string[] keys =
                    JsonSerializer.Deserialize<string[]>(json) ??
                    Array.Empty<string>();

                foreach (string key in keys)
                {
                    _favoriteKeys.Add(key);
                }
            }
            catch
            {
                // A damaged favourites file should never stop Clients loading.
            }
        }

        private void SaveFavorites()
        {
            try
            {
                string json = JsonSerializer.Serialize(
                    _favoriteKeys.OrderBy(x => x).ToArray(),
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                File.WriteAllText(
                    _favoritesFilePath,
                    json);
            }
            catch (Exception ex)
            {
                StatusMessage =
                    "Favourite changed, but it could not be saved: " +
                    ex.Message;
            }
        }

        private static string ClientKey(ClientInfo client)
        {
            if (!string.IsNullOrWhiteSpace(client.MacAddress) &&
                client.MacAddress != "-")
            {
                return NormalizeMac(client.MacAddress);
            }

            return client.IpAddress.Trim();
        }

        private static string NormalizeMac(string? value)
        {
            return new string(
                (value ?? string.Empty)
                    .Where(char.IsLetterOrDigit)
                    .ToArray())
                .ToUpperInvariant();
        }

        private static bool ContainsAny(
            string value,
            params string[] terms)
        {
            return terms.Any(term =>
                value.Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static bool Contains(string? value, string search) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(
                search,
                StringComparison.OrdinalIgnoreCase);

        private static long IpSortKey(string? value)
        {
            if (!System.Net.IPAddress.TryParse(
                value,
                out var address))
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
            return DateTime.TryParse(
                value,
                out DateTime parsed)
                    ? parsed
                    : DateTime.MinValue;
        }
    }
}
