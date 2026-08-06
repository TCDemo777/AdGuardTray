using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RouterPilot.Models;
using RouterPilot.Services;

namespace RouterPilot.ViewModels;

public sealed partial class MaintenanceViewModel : ObservableObject
{
    private readonly MaintenanceOperationService _operations;
    private DashboardViewModel _dashboard;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string activeOperation = string.Empty;

    [ObservableProperty]
    private string lastResult = string.Empty;

    public MaintenanceViewModel(
        MaintenanceOperationService operations,
        MaintenanceHistoryService historyService)
    {
        _operations = operations;
        History = historyService.Entries;
        Actions = new ObservableCollection<MaintenanceActionItem>(
        [
            new(MaintenanceAction.RestartWifi, "Restart Wi-Fi", "Restarts the router wireless interfaces."),
            new(MaintenanceAction.RestartAdGuard, "Restart AdGuard Home", "Briefly restarts DNS filtering."),
            new(MaintenanceAction.ReconnectWan, "Reconnect WAN", "Renews the router WAN interface."),
            new(MaintenanceAction.RebootRouter, "Reboot Router", "Restarts the router and interrupts local connectivity."),
            new(MaintenanceAction.RefreshAll, "Refresh All", "Runs RouterPilot's current dashboard refresh."),
            new(MaintenanceAction.RunDiagnostics, "Run Diagnostics", "Collects the existing safe router support checks.")
        ]);

        _dashboard = new DashboardViewModel();
    }

    public DashboardViewModel Dashboard => _dashboard;

    public ReadOnlyObservableCollection<MaintenanceHistoryEntry> History { get; }

    public ObservableCollection<MaintenanceActionItem> Actions { get; }

    public string WifiStatusText => _dashboard.RouterConnected
        ? RouterPilotStatusPresentation.Active
        : RouterPilotStatusPresentation.NotAvailable;

    public string WifiStatusColour => RouterPilotStatusPresentation.Colour(
        _dashboard.RouterConnected ? RouterPilotStatus.Active : RouterPilotStatus.NotAvailable);

    public async Task ExecuteAsync(MaintenanceActionItem action, Func<Task> refreshAll)
    {
        if (IsBusy || !action.IsAvailable)
            return;

        IsBusy = true;
        ActiveOperation = action.Title;
        UpdateAvailability();

        try
        {
            MaintenanceOperationResult result = await _operations.ExecuteAsync(action.Action, refreshAll);
            LastResult = result.Message;
            action.LastResult = result.Outcome.ToString();
        }
        finally
        {
            ActiveOperation = string.Empty;
            IsBusy = false;
            UpdateAvailability();
        }
    }

    public void AttachDashboard(DashboardViewModel dashboard)
    {
        if (ReferenceEquals(_dashboard, dashboard))
            return;

        _dashboard.PropertyChanged -= Dashboard_PropertyChanged;
        _dashboard = dashboard;
        _dashboard.PropertyChanged += Dashboard_PropertyChanged;
        OnPropertyChanged(nameof(Dashboard));
        OnPropertyChanged(nameof(WifiStatusText));
        OnPropertyChanged(nameof(WifiStatusColour));
        UpdateAvailability();
    }

    private void Dashboard_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.RouterConnected) or
            nameof(DashboardViewModel.AdGuardAvailability))
        {
            OnPropertyChanged(nameof(WifiStatusText));
            OnPropertyChanged(nameof(WifiStatusColour));
            UpdateAvailability();
        }
    }

    partial void OnIsBusyChanged(bool value) => UpdateAvailability();

    private void UpdateAvailability()
    {
        foreach (MaintenanceActionItem action in Actions)
        {
            bool requiresRouter = action.Action is not MaintenanceAction.RefreshAll and not MaintenanceAction.RunDiagnostics;
            bool requiresAdGuard = action.Action == MaintenanceAction.RestartAdGuard;
            bool available = !IsBusy &&
                (!requiresRouter || _dashboard.RouterConnected) &&
                (!requiresAdGuard || _dashboard.IsAdGuardAvailable);

            action.IsAvailable = available;
            action.Availability = available
                ? RouterPilotStatusPresentation.Active
                : IsBusy
                    ? RouterPilotStatusPresentation.Pending
                    : requiresAdGuard && !_dashboard.IsAdGuardAvailable
                        ? RouterPilotStatusPresentation.NotAvailable
                        : RouterPilotStatusPresentation.NotAvailable;
            action.AvailabilityReason = available
                ? "Available on the connected router."
                : IsBusy
                    ? "Another maintenance action is running."
                    : requiresAdGuard && !_dashboard.IsAdGuardAvailable
                        ? "AdGuard Home is not available."
                        : "Connect to the router to use this action.";
        }
    }
}

public sealed partial class MaintenanceActionItem : ObservableObject
{
    public MaintenanceActionItem(MaintenanceAction action, string title, string description)
    {
        Action = action;
        Title = title;
        Description = description;
    }

    public MaintenanceAction Action { get; }
    public string Title { get; }
    public string Description { get; }

    [ObservableProperty]
    private bool isAvailable;

    [ObservableProperty]
    private string availability = RouterPilotStatusPresentation.Pending;

    [ObservableProperty]
    private string availabilityReason = "Loading current router status.";

    [ObservableProperty]
    private string lastResult = RouterPilotStatusPresentation.NotAvailable;
}
