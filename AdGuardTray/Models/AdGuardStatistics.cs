using System.Collections.Generic;

namespace AdGuardTray.Models
{
    public class AdGuardStatistics
    {
        public int TotalQueries { get; set; }

    public int BlockedQueries { get; set; }

        public bool ProtectionEnabled { get; set; }

        public List<AdGuardTimePoint> QueryHistory { get; set; } =
            new();

        public List<AdGuardRankedItem> TopClients { get; set; } =
            new();

        public List<AdGuardRankedItem> TopQueriedDomains { get; set; } =
            new();

        public List<AdGuardRankedItem> TopBlockedDomains { get; set; } =
            new();

        public double BlockPercentage
        {
            get
            {
                if (TotalQueries <= 0)
                {
                    return 0;
                }

                return
                    (double)BlockedQueries /
                    TotalQueries *
                    100;
            }
        }
    }

}
