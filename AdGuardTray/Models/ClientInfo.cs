namespace AdGuardTray.Models
{
    public class ClientInfo
    {
        public string Name { get; set; } = "-";
        public string IpAddress { get; set; } = "-";
        public string MacAddress { get; set; } = "-";
        public int TotalQueries { get; set; }
        public int BlockedQueries { get; set; }

        public double BlockRate =>
            TotalQueries == 0
                ? 0
                : (double)BlockedQueries / TotalQueries * 100;

        public string LastSeen { get; set; } = "-";
    }
}
