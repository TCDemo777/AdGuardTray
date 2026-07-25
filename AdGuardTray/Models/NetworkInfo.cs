namespace AdGuardTray.Models
{
    public class NetworkInfo
    {
        public bool Connected { get; set; }

        public string WanIp { get; set; } = "-";

        public string Gateway { get; set; } = "-";

        public string ExternalDns { get; set; } = "-";

        public string AdvertisedDns { get; set; } = "-";

        public string Latency { get; set; } = "-";


        //
        // Backwards compatibility
        // Existing Dashboard code uses this
        //

        public string DnsServer
        {
            get
            {
                return ExternalDns;
            }

            set
            {
                ExternalDns = value;
            }
        }
    }
}