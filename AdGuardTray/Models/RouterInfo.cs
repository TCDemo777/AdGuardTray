namespace AdGuardTray.Models
{
    public class RouterInfo
    {
        public string Model { get; set; } = "";

        public string FirmwareVersion { get; set; } = "";

        public string Uptime { get; set; } = "";

        public string MemoryUsage { get; set; } = "";

        public string StorageUsage { get; set; } = "";

        public bool Connected { get; set; }
    }
}