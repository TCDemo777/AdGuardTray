using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using RouterPilot.Models;
using RouterPilot.ViewModels;

namespace RouterPilot.Views;

public partial class MaintenanceView : UserControl
{
    private readonly Func<Task> _refreshAll;

    public MaintenanceView(MaintenanceViewModel viewModel, DashboardViewModel dashboard, Func<Task> refreshAll)
    {
        InitializeComponent();
        _refreshAll = refreshAll;
        viewModel.AttachDashboard(dashboard);
        DataContext = viewModel;
    }

    private async void RunAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MaintenanceActionItem action } ||
            DataContext is not MaintenanceViewModel viewModel ||
            !Confirm(action))
        {
            return;
        }

        await viewModel.ExecuteAsync(action, _refreshAll);
    }

    private static bool Confirm(MaintenanceActionItem action)
    {
        (string message, MessageBoxImage icon)? confirmation = action.Action switch
        {
            MaintenanceAction.RestartWifi =>
                ("Restart Wi-Fi now? Connected wireless devices will disconnect temporarily.", MessageBoxImage.Warning),
            MaintenanceAction.RestartAdGuard =>
                ("Restart AdGuard Home now? DNS filtering may be briefly unavailable.", MessageBoxImage.Warning),
            MaintenanceAction.ReconnectWan =>
                ("Reconnect WAN now? Internet access may pause briefly.", MessageBoxImage.Warning),
            MaintenanceAction.RebootRouter =>
                ("Reboot the router now? Internet and local connectivity will be interrupted while it restarts.", MessageBoxImage.Error),
            _ => null
        };

        return confirmation is null || MessageBox.Show(
            confirmation.Value.message,
            action.Title,
            MessageBoxButton.YesNo,
            confirmation.Value.icon) == MessageBoxResult.Yes;
    }
}
