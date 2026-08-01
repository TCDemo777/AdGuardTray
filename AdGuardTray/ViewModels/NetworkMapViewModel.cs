using System.Collections.ObjectModel;
using System.Windows;
using AdGuardTray.Models;
using AdGuardTray.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdGuardTray.ViewModels;

public partial class NetworkMapViewModel : ObservableObject, IDisposable
{
    private readonly NetworkMapService _networkMapService;
    public ObservableCollection<NetworkMapNode> Networks { get; } = new();

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string internetStatus = "Unknown";
    [ObservableProperty] private string routerName = "Router";
    [ObservableProperty] private string routerStatus = "Unknown";
    [ObservableProperty] private string lastUpdated = "Waiting for dashboard data";

    public NetworkMapViewModel(NetworkMapService networkMapService)
    {
        _networkMapService = networkMapService;
        _networkMapService.TopologyChanged += NetworkMapService_TopologyChanged;
        ApplyTopology();
    }

    partial void OnSearchTextChanged(string value) => ApplyTopology();

    private void NetworkMapService_TopologyChanged(object? sender, EventArgs e)
    {
        if (Application.Current.Dispatcher.CheckAccess()) ApplyTopology();
        else Application.Current.Dispatcher.Invoke(ApplyTopology);
    }

    private void ApplyTopology()
    {
        NetworkTopology topology = _networkMapService.Topology;
        InternetStatus = topology.Internet.Status;
        RouterName = topology.Router.Name;
        RouterStatus = topology.Router.Status;
        LastUpdated = topology.GeneratedAt == default
            ? "Waiting for dashboard data"
            : "Updated " + topology.GeneratedAt.ToLocalTime().ToString("HH:mm:ss");
        string search = SearchText.Trim();
        Networks.Clear();
        foreach (NetworkMapNode group in topology.Networks)
        {
            NetworkMapNode[] matching = search.Length == 0
                ? group.Children.ToArray()
                : group.Children.Where(device =>
                    Contains(device.Name, search) || Contains(device.IpAddress, search) ||
                    Contains(device.Manufacturer, search) || Contains(device.DeviceType, search) ||
                    Contains(device.MacAddress, search) || Contains(group.Name, search)).ToArray();
            if (matching.Length == 0 && search.Length > 0) continue;
            Networks.Add(new NetworkMapNode
            {
                Id = group.Id, Type = group.Type, Name = group.Name,
                Status = $"{matching.Length} connected", Children = matching
            });
        }
    }

    private static bool Contains(string value, string search) =>
        value.Contains(search, StringComparison.OrdinalIgnoreCase);

    public void Dispose() =>
        _networkMapService.TopologyChanged -= NetworkMapService_TopologyChanged;
}
