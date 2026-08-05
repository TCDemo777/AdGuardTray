namespace RouterPilot.Models
{
    public class RouterInfo
    {
        public string Model { get; set; } = "-";

        public string Hostname { get; set; } = "-";

        public string Firmware { get; set; } = "-";

        public string Uptime { get; set; } = "-";

        public string CpuUsage { get; set; } = "-";

        public double? CpuUsagePercent { get; set; }

        public bool CpuUtilisationPending { get; set; }

        public double? LoadAverage1Minute { get; set; }

        public int? LogicalProcessorCount { get; set; }

        public string Temperature { get; set; } = "-";

        public string LoadAverage { get; set; } = "-";

        public string MemoryUsage { get; set; } = "-";

        public string MemoryUsed { get; set; } = "-";

        public string MemoryCache { get; set; } = "-";

        public string StorageUsage { get; set; } = "-";


        //
        // Backwards compatibility
        // DiagnosticsWindow currently expects these
        //

        public string WanIp { get; set; } = "-";

        public string Gateway { get; set; } = "-";

        public string DnsServer { get; set; } = "-";

        public string Latency { get; set; } = "-";
    }
}
