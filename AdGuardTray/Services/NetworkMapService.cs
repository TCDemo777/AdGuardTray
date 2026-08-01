using AdGuardTray.Models;

namespace AdGuardTray.Services;

public sealed class NetworkMapService
{
    private readonly object _syncRoot = new();
    private NetworkTopology _topology = NetworkTopology.Empty;

    public event EventHandler? TopologyChanged;

    public NetworkTopology Topology
    {
        get { lock (_syncRoot) return _topology; }
    }

    public void UpdateTopology(
        bool internetConnected,
        bool routerConnected,
        string routerName,
        IEnumerable<DeviceHistoryRecord> deviceHistory,
        IEnumerable<WifiRadioInfo> wifiRadios,
        IReadOnlyDictionary<string, DeviceBehaviourProfile> behaviourProfiles)
    {
        DeviceHistoryRecord[] onlineDevices = deviceHistory
            .Where(device => device.IsCurrentlyOnline)
            .ToArray();
        WifiRadioInfo[] radios = wifiRadios.ToArray();
        var groups = new Dictionary<string, List<NetworkMapNode>>(StringComparer.OrdinalIgnoreCase);
        foreach (DeviceHistoryRecord device in onlineDevices)
        {
            string mac = DeviceHistoryService.NormalizeMacAddress(device.MacAddress);
            WifiClientInfo? station = radios.SelectMany(radio => radio.Clients)
                .FirstOrDefault(client =>
                    DeviceHistoryService.NormalizeMacAddress(client.MacAddress) == mac &&
                    (client.IsActiveStation || client.IsCurrentlyOnline));
            string network = ClassifyNetwork(device, station);
            if (!groups.TryGetValue(network, out List<NetworkMapNode>? devices))
                groups[network] = devices = new List<NetworkMapNode>();
            behaviourProfiles.TryGetValue(mac, out DeviceBehaviourProfile? profile);
            devices.Add(new NetworkMapNode
            {
                Id = "device:" + mac,
                Type = NetworkMapNodeType.Device,
                Name = First(device.FriendlyName, device.Hostname, station?.Name, device.MacAddress),
                Status = "Online",
                Manufacturer = First(device.Manufacturer, "Unknown manufacturer"),
                DeviceType = First(device.DeviceType, "Unknown device"),
                IpAddress = First(station?.IpAddress, device.LastIpAddress, "Unavailable"),
                MacAddress = device.MacAddress,
                LastSeen = device.LastSeen,
                TypicalOnlinePeriod = profile?.TypicalOnlineTimeDisplay ?? "Not enough history"
            });
        }

        var representedMacs = onlineDevices
            .Select(device => DeviceHistoryService.NormalizeMacAddress(device.MacAddress))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (WifiClientInfo station in radios.SelectMany(radio => radio.Clients)
                     .Where(client => client.IsActiveStation ||
                                      (client.IsOnlineStateKnown && client.IsCurrentlyOnline)))
        {
            string mac = DeviceHistoryService.NormalizeMacAddress(station.MacAddress);
            if (mac.Length != 12 || !representedMacs.Add(mac)) continue;
            string network = ClassifyNetwork(new DeviceHistoryRecord(), station);
            if (!groups.TryGetValue(network, out List<NetworkMapNode>? devices))
                groups[network] = devices = new List<NetworkMapNode>();
            behaviourProfiles.TryGetValue(mac, out DeviceBehaviourProfile? profile);
            devices.Add(new NetworkMapNode
            {
                Id = "device:" + mac,
                Type = NetworkMapNodeType.Device,
                Name = First(station.Name, station.MacAddress),
                Status = "Online",
                Manufacturer = "Unknown manufacturer",
                DeviceType = "Unknown device",
                IpAddress = First(station.IpAddress, "Unavailable"),
                MacAddress = station.MacAddress,
                LastSeen = DateTimeOffset.Now,
                TypicalOnlinePeriod = profile?.TypicalOnlineTimeDisplay ?? "Not enough history"
            });
        }

        NetworkMapNode[] networkNodes = groups
            .OrderBy(group => GroupOrder(group.Key))
            .ThenBy(group => group.Key)
            .Select(group => new NetworkMapNode
            {
                Id = "network:" + group.Key.ToLowerInvariant().Replace(' ', '-'),
                Type = NetworkMapNodeType.Network,
                Name = group.Key,
                Status = $"{group.Value.Count} connected",
                Children = group.Value.OrderBy(device => device.Name).ToArray()
            }).ToArray();
        var internetNode = new NetworkMapNode
        {
            Id = "internet", Type = NetworkMapNodeType.Internet,
            Name = "Internet", Status = internetConnected ? "Online" : "Offline"
        };
        var routerNode = new NetworkMapNode
        {
            Id = "router", Type = NetworkMapNodeType.Router,
            Name = First(routerName, "Router"), Status = routerConnected ? "Online" : "Offline",
            Children = networkNodes
        };
        var edges = new List<NetworkMapEdge> { new("internet", "router") };
        foreach (NetworkMapNode group in networkNodes)
        {
            edges.Add(new NetworkMapEdge("router", group.Id));
            edges.AddRange(group.Children.Select(device => new NetworkMapEdge(group.Id, device.Id)));
        }
        lock (_syncRoot)
        {
            _topology = new NetworkTopology
            {
                GeneratedAt = DateTimeOffset.Now,
                Internet = internetNode,
                Router = routerNode,
                Networks = networkNodes,
                Edges = edges
            };
        }
        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string ClassifyNetwork(DeviceHistoryRecord device, WifiClientInfo? station)
    {
        string value = First(station?.Ssid, device.LastSsid, device.LastNetworkName, device.DeviceType);
        if (Contains(value, "vpn")) return "VPN";
        if (Contains(value, "guest")) return "Guest";
        if (Contains(value, "iot")) return "IoT";
        if (station is null || Contains(value, "ethernet") || Contains(value, "wired") || Contains(value, "lan"))
            return "Ethernet";
        return "Main Wi-Fi";
    }

    private static bool Contains(string value, string search) =>
        value.Contains(search, StringComparison.OrdinalIgnoreCase);
    private static int GroupOrder(string group) => group switch
    {
        "Ethernet" => 0, "Main Wi-Fi" => 1, "IoT" => 2,
        "Guest" => 3, "VPN" => 4, _ => 5
    };
    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value) && value != "-") ?? string.Empty;
}
