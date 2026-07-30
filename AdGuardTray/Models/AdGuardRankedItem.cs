namespace AdGuardTray.Models
{
    public class AdGuardRankedItem
    {
        public int Rank { get; set; }

        public string Name { get; set; } = "-";

        public int Count { get; set; }

        public double RelativePercent { get; set; }

        public string DisplayName
        {
            get
            {
                SplitIdentity(
                    out string displayName,
                    out _);

                return displayName;
            }
        }

        public string Address
        {
            get
            {
                SplitIdentity(
                    out _,
                    out string address);

                return address;
            }
        }

        private void SplitIdentity(
            out string displayName,
            out string address)
        {
            string value =
                string.IsNullOrWhiteSpace(Name)
                    ? "-"
                    : Name.Trim();

            int openingBracket =
                value.LastIndexOf(" (",
                    System.StringComparison.Ordinal);

            if (openingBracket > 0 &&
                value.EndsWith(
                    ")",
                    System.StringComparison.Ordinal))
            {
                displayName =
                    value[..openingBracket].Trim();

                address =
                    value[(openingBracket + 2)..^1].Trim();

                return;
            }

            displayName =
                value;

            address =
                LooksLikeAddress(value)
                    ? value
                    : string.Empty;
        }

        private static bool LooksLikeAddress(string value)
        {
            return
                System.Net.IPAddress.TryParse(
                    value,
                    out _) ||
                value.Contains(':');
        }
    }
}
