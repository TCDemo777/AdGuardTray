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

        // Presentation metadata populated by ClientsViewModel.
        public string DeviceIcon { get; set; } = "●";
        public string DeviceType { get; set; } = "Unknown device";
        public string Manufacturer { get; set; } = "Unknown manufacturer";
        public string HealthText { get; set; } = "Unknown";
        public string HealthColour { get; set; } = "#687386";
        public bool IsFavorite { get; set; }

        public string FavoriteGlyph =>
            IsFavorite ? "★" : "☆";

        public string FavoriteToolTip =>
            IsFavorite
                ? "Remove from favourites"
                : "Add to favourites";
    }
}
