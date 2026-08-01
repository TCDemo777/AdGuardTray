using System.Windows;
using System.Windows.Controls;
using AdGuardTray.Models;
using AdGuardTray.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AdGuardTray.Views;

public partial class NetworkMapView : UserControl
{
    private readonly NetworkMapViewModel _viewModel;
    public NetworkMapView()
    {
        InitializeComponent();
        _viewModel = ((App)Application.Current).Services.GetRequiredService<NetworkMapViewModel>();
        DataContext = _viewModel;
        Unloaded += (_, _) => _viewModel.Dispose();
    }

    private void Device_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NetworkMapNode node }) return;
        var client = new ClientInfo
        {
            Name = node.Name, IpAddress = node.IpAddress, MacAddress = node.MacAddress,
            Manufacturer = node.Manufacturer, DeviceType = node.DeviceType,
            LastObservedUtc = node.LastSeen?.UtcDateTime ?? default,
            LastSeen = node.LastSeenDisplay
        };
        new ClientDetailsWindow(client) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
