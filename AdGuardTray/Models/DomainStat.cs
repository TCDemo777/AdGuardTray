namespace AdGuardTray.Models
{
    public class DomainStat
    {
        public int Rank { get; set; }
        public string Domain { get; set; } = "-";
        public int Count { get; set; }
        public double Percentage { get; set; }
    }
}
