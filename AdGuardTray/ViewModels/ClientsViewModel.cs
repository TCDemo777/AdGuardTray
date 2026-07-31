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
        private readonly Dictionary<string, WifiClientInfo> _liveClientLookup =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ClientActivityItem>> _clientActivityHistory =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (int Total, int Blocked)> _lastActivityTotals =
            new(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<ClientInfo> Clients { get; } = new();
        public ObservableCollection<ClientActivityItem> SelectedClientActivity { get; } = new();

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
        private string selectedSortOption = "Blocked queries";

        [ObservableProperty]
        private bool sortDescending = true;

        [ObservableProperty]
        private bool showFavoritesOnly;

        [ObservableProperty]
        private ClientInfo? selectedClient;

        [ObservableProperty]
        private string selectedClientWifiNetwork = "—";

        [ObservableProperty]
        private string selectedClientSignal = "—";

        [ObservableProperty]
        private string statusMessage = "No client data loaded.";

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private bool isPinging;

        [ObservableProperty]
        private bool isWaking;

        [ObservableProperty]
        private string pingResult = "Select a client to run a connectivity check.";

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

                string? selectedKey = SelectedClient is null
                    ? null
                    : ClientKey(SelectedClient);

                List<ClientInfo> clients =
                    await _routerManager.GetAdGuardClientsAsync();

                // Use the same per-SSID mapping as the Network tab so selected
                // clients receive the resolved Wi-Fi name, interface and signal.
                List<WifiRadioInfo> wifiNetworks =
                    await _routerManager.GetWifiRadiosAsync();

                // Flatten the per-network client lists while explicitly carrying
                // the parent SSID/band/interface onto each client.  The Network
                // view can display a client under an SSID even when the GL.iNet
                // payload omits SSID on the child object, so relying on the child
                // record alone loses the network name in the Clients view.
                List<WifiClientInfo> liveClients = wifiNetworks
                    .SelectMany(network => network.Clients.Select(client =>
                    {
                        client.Ssid = HasUsefulValue(client.Ssid)
                            ? client.Ssid
                            : network.Ssid;
                        client.Band = HasUsefulValue(client.Band)
                            ? client.Band
                            : network.Band;
                        client.Interface = HasUsefulValue(client.Interface)
                            ? client.Interface
                            : network.Interface;
                        return client;
                    }))
                    .ToList();

                // Retain Ethernet and any firmware-only clients that are not
                // represented in the Wi-Fi network collection.
                List<WifiClientInfo> inventoryClients =
                    await _routerManager.GetGlClientInventoryAsync();

                foreach (WifiClientInfo inventoryClient in inventoryClients)
                {
                    bool alreadyPresent = liveClients.Any(item =>
                        ClientIdentityEquals(item, inventoryClient));

                    if (!alreadyPresent)
                    {
                        liveClients.Add(inventoryClient);
                    }
                }

                RebuildLiveClientLookup(liveClients);

                foreach (ClientInfo client in clients)
                {
                    ApplyLiveConnectionDetails(client, liveClients);
                    EnrichClient(client);
                    RecordActivitySnapshot(client);
                }

                _allClients.Clear();
                _allClients.AddRange(clients);

                ApplyFilterAndSort(selectedKey);

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

            // Use the generated ObservableProperty setter so change
            // notifications and OnSelectedSortOptionChanged run correctly.
            SelectedSortOption = option;
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
        private async Task PingSelectedClientAsync()
        {
            if (SelectedClient is null)
            {
                PingResult = "Select a client first.";
                return;
            }

            if (_routerManager is null)
            {
                PingResult = "Load the client list before running a ping.";
                return;
            }

            if (IsPinging)
            {
                return;
            }

            IsPinging = true;
            PingResult = $"Pinging {SelectedClient.IpAddress}...";

            try
            {
                PingResult = await _routerManager.PingClientAsync(
                    SelectedClient.IpAddress);
                AddActivityEvent(
                    SelectedClient,
                    "Ping",
                    "Connectivity check completed",
                    PingResult);
            }
            catch (Exception ex)
            {
                PingResult = "Ping failed: " + ex.Message;
            }
            finally
            {
                IsPinging = false;
            }
        }


        [RelayCommand]
        private async Task WakeSelectedClientAsync()
        {
            if (SelectedClient is null)
            {
                PingResult = "Select a client first.";
                return;
            }

            if (_routerManager is null)
            {
                PingResult = "Load the client list before sending Wake-on-LAN.";
                return;
            }

            if (IsWaking)
            {
                return;
            }

            IsWaking = true;
            PingResult = $"Sending Wake-on-LAN to {SelectedClient.Name}...";

            try
            {
                PingResult = await _routerManager.WakeClientAsync(
                    SelectedClient.MacAddress);
                AddActivityEvent(
                    SelectedClient,
                    "Wake",
                    "Wake-on-LAN request sent",
                    PingResult);
            }
            catch (Exception ex)
            {
                PingResult = "Wake-on-LAN failed: " + ex.Message;
            }
            finally
            {
                IsWaking = false;
            }
        }

        [RelayCommand]
        private void ToggleSortDirection()
        {
            SortDescending = !SortDescending;
            OnPropertyChanged(nameof(SortDirectionText));
            ApplyFilterAndSort();
        }

        partial void OnSelectedClientChanged(ClientInfo? value)
        {
            PingResult = value is null
                ? "Select a client to run a connectivity check."
                : $"Ready to ping or wake {value.Name} ({value.IpAddress}).";

            UpdateSelectedClientConnectionDetails(value);
            LoadSelectedClientActivity(value);
        }

        private async Task RefreshSelectedClientWifiDetailsAsync(ClientInfo? client)
        {
            if (client is null || _routerManager is null)
            {
                return;
            }

            string selectionKey = ClientKey(client);
            SelectedClientWifiNetwork = "Looking up…";
            SelectedClientSignal = "Looking up…";

            try
            {
                WifiClientInfo? live = await _routerManager.GetWifiClientDetailsAsync(
                    client.MacAddress,
                    client.IpAddress);

                if (SelectedClient is null ||
                    !ClientKey(SelectedClient).Equals(selectionKey, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                SelectedClientWifiNetwork = live is not null && HasUsefulValue(live.Ssid)
                    ? live.Ssid
                    : HasUsefulValue(client.WifiNetwork) ? client.WifiNetwork : "—";

                SelectedClientSignal = live is not null && HasUsefulValue(live.Signal)
                    ? live.Signal
                    : HasUsefulValue(client.SignalStrength) ? client.SignalStrength : "Not reported";
            }
            catch
            {
                if (SelectedClient is not null &&
                    ClientKey(SelectedClient).Equals(selectionKey, StringComparison.OrdinalIgnoreCase))
                {
                    UpdateSelectedClientConnectionDetails(client);
                }
            }
        }

        private void RecordActivitySnapshot(ClientInfo client)
        {
            string key = ClientKey(client);
            var current = (client.TotalQueries, client.BlockedQueries);

            if (_lastActivityTotals.TryGetValue(key, out var previous))
            {
                int queryDelta = Math.Max(0, current.TotalQueries - previous.Total);
                int blockedDelta = Math.Max(0, current.BlockedQueries - previous.Blocked);

                if (queryDelta > 0 || blockedDelta > 0)
                {
                    AddActivityEvent(
                        client,
                        "DNS",
                        $"+{queryDelta} queries · +{blockedDelta} blocked",
                        $"Totals: {current.TotalQueries} queries, {current.BlockedQueries} blocked");
                }
            }
            else
            {
                AddActivityEvent(
                    client,
                    "Snapshot",
                    "Client activity loaded",
                    $"{current.TotalQueries} queries · {current.BlockedQueries} blocked");
            }

            _lastActivityTotals[key] = current;
        }

        private void AddActivityEvent(
            ClientInfo client,
            string eventType,
            string summary,
            string detail)
        {
            string key = ClientKey(client);
            if (!_clientActivityHistory.TryGetValue(key, out List<ClientActivityItem>? history))
            {
                history = new List<ClientActivityItem>();
                _clientActivityHistory[key] = history;
            }

            history.Insert(0, new ClientActivityItem
            {
                Timestamp = DateTime.Now,
                EventType = eventType,
                Summary = summary,
                Detail = detail
            });

            if (history.Count > 20)
            {
                history.RemoveRange(20, history.Count - 20);
            }

            if (SelectedClient is not null &&
                ClientKey(SelectedClient).Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                LoadSelectedClientActivity(SelectedClient);
            }
        }

        private void LoadSelectedClientActivity(ClientInfo? client)
        {
            SelectedClientActivity.Clear();

            if (client is null)
            {
                return;
            }

            string key = ClientKey(client);
            if (!_clientActivityHistory.TryGetValue(key, out List<ClientActivityItem>? history))
            {
                return;
            }

            foreach (ClientActivityItem item in history.Take(8))
            {
                SelectedClientActivity.Add(item);
            }
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

        private void ApplyFilterAndSort(string? preferredSelectionKey = null)
        {
            string? selectionKey = preferredSelectionKey ??
                (SelectedClient is null ? null : ClientKey(SelectedClient));
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

            if (!string.IsNullOrWhiteSpace(selectionKey))
            {
                SelectedClient = Clients.FirstOrDefault(client =>
                    ClientKey(client).Equals(
                        selectionKey,
                        StringComparison.OrdinalIgnoreCase));
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

        private static bool ClientIdentityEquals(
            WifiClientInfo left,
            WifiClientInfo right)
        {
            string leftMac = NormaliseMac(left.MacAddress);
            string rightMac = NormaliseMac(right.MacAddress);

            if (leftMac.Length == 12 && rightMac.Length == 12)
            {
                return leftMac.Equals(
                    rightMac,
                    StringComparison.OrdinalIgnoreCase);
            }

            return !string.IsNullOrWhiteSpace(left.IpAddress) &&
                   left.IpAddress != "-" &&
                   left.IpAddress.Equals(
                       right.IpAddress,
                       StringComparison.OrdinalIgnoreCase);
        }

        private void RebuildLiveClientLookup(IEnumerable<WifiClientInfo> liveClients)
        {
            _liveClientLookup.Clear();

            foreach (WifiClientInfo live in liveClients
                .OrderByDescending(item => HasUsefulValue(item.Ssid))
                .ThenByDescending(item => HasUsefulValue(item.Signal)))
            {
                string macKey = NormaliseMac(live.MacAddress);
                if (macKey.Length == 12 && !_liveClientLookup.ContainsKey("mac:" + macKey))
                {
                    _liveClientLookup["mac:" + macKey] = live;
                }

                if (!string.IsNullOrWhiteSpace(live.IpAddress) && live.IpAddress != "-")
                {
                    string ipKey = "ip:" + live.IpAddress.Trim();
                    if (!_liveClientLookup.ContainsKey(ipKey))
                    {
                        _liveClientLookup[ipKey] = live;
                    }
                }

                string nameKey = NormaliseClientName(live.Name);
                if (nameKey.Length > 0 && !_liveClientLookup.ContainsKey("name:" + nameKey))
                {
                    _liveClientLookup["name:" + nameKey] = live;
                }
            }

            UpdateSelectedClientConnectionDetails(SelectedClient);
        }

        private void UpdateSelectedClientConnectionDetails(ClientInfo? client)
        {
            if (client is null)
            {
                SelectedClientWifiNetwork = "—";
                SelectedClientSignal = "—";
                return;
            }

            WifiClientInfo? live = null;
            string macKey = NormaliseMac(client.MacAddress);
            if (macKey.Length == 12)
            {
                _liveClientLookup.TryGetValue("mac:" + macKey, out live);
            }

            if (live is null && !string.IsNullOrWhiteSpace(client.IpAddress) && client.IpAddress != "-")
            {
                _liveClientLookup.TryGetValue("ip:" + client.IpAddress.Trim(), out live);
            }

            if (live is null)
            {
                string nameKey = NormaliseClientName(client.Name);
                if (nameKey.Length > 0)
                {
                    _liveClientLookup.TryGetValue("name:" + nameKey, out live);
                }
            }

            SelectedClientWifiNetwork = live is not null && HasUsefulValue(live.Ssid)
                ? live.Ssid
                : HasUsefulValue(client.WifiNetwork) ? client.WifiNetwork : "—";

            SelectedClientSignal = live is not null && HasUsefulValue(live.Signal)
                ? live.Signal
                : HasUsefulValue(client.SignalStrength) ? client.SignalStrength : "—";
        }

        private static void ApplyLiveConnectionDetails(
            ClientInfo client,
            IEnumerable<WifiClientInfo> liveClients)
        {
            string clientMac = NormaliseMac(client.MacAddress);

            WifiClientInfo? live = liveClients
                .Where(item =>
                {
                    string itemMac = NormaliseMac(item.MacAddress);

                    bool macMatches =
                        clientMac.Length == 12 &&
                        itemMac.Length == 12 &&
                        itemMac.Equals(
                            clientMac,
                            StringComparison.OrdinalIgnoreCase);

                    bool ipMatches =
                        !string.IsNullOrWhiteSpace(client.IpAddress) &&
                        client.IpAddress != "-" &&
                        item.IpAddress.Equals(
                            client.IpAddress,
                            StringComparison.OrdinalIgnoreCase);

                    string clientName = NormaliseClientName(client.Name);
                    string liveName = NormaliseClientName(item.Name);
                    bool nameMatches =
                        clientName.Length > 0 &&
                        liveName.Length > 0 &&
                        clientName.Equals(liveName, StringComparison.OrdinalIgnoreCase);

                    return macMatches || ipMatches || nameMatches;
                })
                // Prefer the per-SSID record used by the Network tab over the
                // more limited GL.iNet inventory fallback.
                .OrderByDescending(item => HasUsefulValue(item.Ssid))
                .ThenByDescending(item => HasUsefulValue(item.Signal))
                .ThenByDescending(item => HasUsefulValue(item.Interface))
                .FirstOrDefault();

            if (live is null)
            {
                return;
            }

            if (client.MacAddress == "-") client.MacAddress = live.MacAddress;
            if (client.IpAddress == "-") client.IpAddress = live.IpAddress;
            if ((client.Name == "-" || client.Name.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) &&
                !live.Name.Equals("Unknown device", StringComparison.OrdinalIgnoreCase))
            {
                client.Name = live.Name;
            }

            client.ConnectionType = live.Band;
            client.WifiNetwork = live.Ssid;
            client.SignalStrength = live.Signal;
            client.LiveInterface = live.Interface;
        }


        private static string NormaliseMac(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "-")
            {
                return string.Empty;
            }

            return new string(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        private static string NormaliseClientName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                value == "-" ||
                value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Unknown device", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return new string(value
                .Trim()
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        private static bool HasUsefulValue(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            value != "-" &&
            !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
            !value.Equals("Unknown network", StringComparison.OrdinalIgnoreCase);

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
