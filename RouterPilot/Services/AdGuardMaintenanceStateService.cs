using CommunityToolkit.Mvvm.ComponentModel;
using RouterPilot.Models;

namespace RouterPilot.Services;

public sealed partial class AdGuardMaintenanceStateService : ObservableObject
{
    [ObservableProperty]
    private AdGuardMaintenanceState state;

    public void BeginRestart() => State = AdGuardMaintenanceState.Restarting;
    public void CompleteRestart() => State = AdGuardMaintenanceState.None;
    public void FailRestart() => State = AdGuardMaintenanceState.Failed;
}
