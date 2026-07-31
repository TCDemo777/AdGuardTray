namespace AdGuardTray.Models
{
    public class WifiRadioInfo
    {
        public string Radio { get; set; } = "-";
        public string Ssid { get; set; } = "-";
        public string Band { get; set; } = "-";
        public string Channel { get; set; } = "-";
        public string Status { get; set; } = "Unavailable";
        public int ClientCount { get; set; }
    }
}
