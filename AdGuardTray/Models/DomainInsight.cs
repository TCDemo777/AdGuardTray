using System.Collections.Generic;

namespace AdGuardTray.Models
{
    public class DomainInsight
    {
        public string Domain { get; set; } = "-";
        public int TotalQueries { get; set; }
        public int BlockedQueries { get; set; }
        public double BlockRate =>
            TotalQueries == 0
                ? 0
                : (double)BlockedQueries / TotalQueries * 100;

        public string FirstSeen { get; set; } = "-";
        public string LastSeen { get; set; } = "-";
        public string ResultSummary { get; set; } = "-";
        public List<string> Clients { get; set; } = new();

        public string ClientSummary =>
            Clients.Count == 0
                ? "No clients"
                : string.Join(", ", Clients);
    }
}
