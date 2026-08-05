using System.Collections.ObjectModel;

namespace RouterPilot.Models
{
    public class WifiRadioInfo
    {
        public string Radio { get; set; } = "-";
        public string Interface { get; set; } = "-";
        public string Ssid { get; set; } = "-";
        public string Band { get; set; } = "-";
        public string Channel { get; set; } = "-";
        public string Security { get; set; } = "-";
        public string Status { get; set; } = RouterPilotStatusPresentation.NotAvailable;

        public string StatusDisplay => Status.Trim().ToLowerInvariant() switch
        {
            "disabled" or "down" => RouterPilotStatusPresentation.Disabled,
            "active" or "configured" or "online" or "running" or "up" =>
                RouterPilotStatusPresentation.Active,
            _ => RouterPilotStatusPresentation.NotAvailable
        };

        public string StatusColour => RouterPilotStatusPresentation.Colour(Status.Trim().ToLowerInvariant() switch
        {
            "disabled" or "down" => RouterPilotStatus.Disabled,
            "active" or "configured" or "online" or "running" or "up" =>
                RouterPilotStatus.Active,
            _ => RouterPilotStatus.NotAvailable
        });
        public int ClientCount => Clients.Count;
        public ObservableCollection<WifiClientInfo> Clients { get; } = new();
    }
}
