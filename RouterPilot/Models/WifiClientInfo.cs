namespace RouterPilot.Models
{
    public class WifiClientInfo
    {
        public string Name { get; set; } = "Unknown device";
        public string IpAddress { get; set; } = "-";
        public string MacAddress { get; set; } = "-";
        public string Signal { get; set; } = "-";
        public string Band { get; set; } = "-";
        public string Interface { get; set; } = "-";
        public string Ssid { get; set; } = "-";
    }
}
