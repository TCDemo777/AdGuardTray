using System.Collections.ObjectModel;

namespace AdGuardTray.Models
{
    public class WifiRadioInfo
    {
        public string Radio { get; set; } = "-";
        public string Interface { get; set; } = "-";
        public string Ssid { get; set; } = "-";
        public string Band { get; set; } = "-";
        public string Channel { get; set; } = "-";
        public string Security { get; set; } = "-";
        public string Status { get; set; } = "Unavailable";
        public int ClientCount => Clients.Count;
        public ObservableCollection<WifiClientInfo> Clients { get; } = new();
    }
}
