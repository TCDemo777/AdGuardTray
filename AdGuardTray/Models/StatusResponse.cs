using System.Text.Json.Serialization;

namespace AdGuardTray.Models;

public class StatsResponse
{
    [JsonPropertyName("num_dns_queries")]
    public int DnsQueries { get; set; }

    [JsonPropertyName("num_blocked_filtering")]
    public int BlockedFiltering { get; set; }

    [JsonPropertyName("num_replaced_safebrowsing")]
    public int SafeBrowsingBlocked { get; set; }

    [JsonPropertyName("num_replaced_parental")]
    public int ParentalBlocked { get; set; }

    [JsonPropertyName("avg_processing_time")]
    public double AverageProcessingTime { get; set; }

    [JsonPropertyName("top_clients")]
    public List<Dictionary<string, int>> TopClients { get; set; } = [];

    [JsonPropertyName("top_blocked_domains")]
    public List<Dictionary<string, int>> TopBlockedDomains { get; set; } = [];

    [JsonPropertyName("top_queried_domains")]
    public List<Dictionary<string, int>> TopQueriedDomains { get; set; } = [];
}