using AdGuardTray.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;

namespace AdGuardTray.Services;

public sealed class AdGuardAvailabilityService : ObservableObject
{
    private int _state = (int)AdGuardAvailabilityState.Unavailable;

    public AdGuardAvailabilityState State =>
        (AdGuardAvailabilityState)Volatile.Read(ref _state);

    public bool IsAvailable => State == AdGuardAvailabilityState.Available;

    public string DisplayText => RouterPilotStatusPresentation.Text(
        IsAvailable
            ? RouterPilotStatus.Active
            : RouterPilotStatus.NotAvailable);

    public void SetState(AdGuardAvailabilityState state)
    {
        int previous = Interlocked.Exchange(ref _state, (int)state);
        if (previous == (int)state)
        {
            return;
        }

        void Notify()
        {
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(IsAvailable));
            OnPropertyChanged(nameof(DisplayText));
        }

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Notify);
        }
        else
        {
            Notify();
        }
    }
}
