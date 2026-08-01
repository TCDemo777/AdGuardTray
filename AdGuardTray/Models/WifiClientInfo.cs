namespace AdGuardTray.Models
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

        // A lease, cached inventory row or remembered address is not proof of
        // connectivity. These flags describe the live source that observed it.
        public bool IsCurrentlyOnline { get; set; }
        public bool IsOnlineStateKnown { get; set; }
        public bool IsActiveStation { get; set; }
    }
}
