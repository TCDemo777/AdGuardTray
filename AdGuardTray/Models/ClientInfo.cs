using System;

namespace AdGuardTray.Models
{
    public class ClientInfo
    {
        public string Name { get; set; } = "-";
        public string RouterName { get; set; } = "-";
        public string Notes { get; set; } = string.Empty;
        public string CustomCategory { get; set; } = string.Empty;
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastObservedUtc { get; set; }
        public string IpAddress { get; set; } = "-";
        public string MacAddress { get; set; } = "-";
        public int TotalQueries { get; set; }
        public int BlockedQueries { get; set; }

        public double BlockRate =>
            TotalQueries == 0
                ? 0
                : (double)BlockedQueries / TotalQueries * 100;

        public string LastSeen { get; set; } = "-";

        // Set by RouterManager when AdGuard Home query logging is available.
        // Query totals may still be populated from /control/stats when false.
        public bool QueryLogAvailable { get; set; } = true;

        public string LastSeenDisplay =>
            QueryLogAvailable
                ? LastSeen
                : "Query log disabled";

        public string BlockedQueriesDisplay =>
            QueryLogAvailable
                ? BlockedQueries.ToString()
                : "—";

        public string BlockRateDisplay =>
            QueryLogAvailable
                ? $"{BlockRate:F1}%"
                : "—";

        public string ActivityAvailabilityToolTip =>
            QueryLogAvailable
                ? "Live values from the AdGuard Home query log."
                : "Enable query logging in About > Diagnostics to collect blocked and last-seen activity.";

        // Presentation metadata populated by ClientsViewModel.
        public string DeviceIcon { get; set; } = "●";
        public string DeviceType { get; set; } = "Unknown device";
        public string Manufacturer { get; set; } = "Unknown manufacturer";
        public string HealthText { get; set; } = "Unknown";
        public string HealthColour { get; set; } = "#687386";
        public bool IsFavorite { get; set; }

        // Live connection metadata from the GL.iNet client inventory.
        public string ConnectionType { get; set; } = "Unknown";
        public string WifiNetwork { get; set; } = "-";
        public string SignalStrength { get; set; } = "-";
        public string LiveInterface { get; set; } = "-";

        public string FirstSeenDisplay =>
            FirstSeenUtc == default ? "—" : FirstSeenUtc.ToLocalTime().ToString("g");

        public string LastObservedDisplay =>
            LastObservedUtc == default ? "—" : LastObservedUtc.ToLocalTime().ToString("g");

        public string FavoriteGlyph =>
            IsFavorite ? "★" : "☆";

        public string FavoriteToolTip =>
            IsFavorite
                ? "Remove from favourites"
                : "Add to favourites";
    }
}
