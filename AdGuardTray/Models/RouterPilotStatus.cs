namespace AdGuardTray.Models;

/// <summary>
/// Presentation vocabulary for application-level connection and service state.
/// Keep this separate from domain state such as AdGuard availability or a
/// protection pause so those states can retain their existing behaviour.
/// </summary>
public enum RouterPilotStatus
{
    Connected,
    Active,
    Disabled,
    NotAvailable,
    Error,
    Pending
}

public static class RouterPilotStatusPresentation
{
    public const string Connected = "Connected";
    public const string Active = "Active";
    public const string Disabled = "Disabled";
    public const string NotAvailable = "N/A";
    public const string Error = "Error";
    public const string Pending = "Pending";

    public static string Text(RouterPilotStatus status) => status switch
    {
        RouterPilotStatus.Connected => Connected,
        RouterPilotStatus.Active => Active,
        RouterPilotStatus.Disabled => Disabled,
        RouterPilotStatus.NotAvailable => NotAvailable,
        RouterPilotStatus.Error => Error,
        RouterPilotStatus.Pending => Pending,
        _ => NotAvailable
    };

    // These match the existing success, warning, neutral and error colours
    // already used by status pills and summary cards.
    public static string Colour(RouterPilotStatus status) => status switch
    {
        RouterPilotStatus.Connected or RouterPilotStatus.Active => "#16803C",
        RouterPilotStatus.Disabled or RouterPilotStatus.Pending => "#B26A00",
        RouterPilotStatus.Error => "#C62828",
        _ => "#687386"
    };
}
