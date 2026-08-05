namespace RouterPilot.Models
{
    public class AdGuardStatus
    {
        public bool IsRunning { get; set; }

        public string ServiceStatus { get; set; } = "";

        public string Process { get; set; } = "";

        public string Version { get; set; } = "";

        public string RouterIp { get; set; } = "";

        public string Username { get; set; } = "";
    }
}
