using System.Text.Json.Serialization;

namespace AdGuardTray.Models;

public class StatusResponse
{
    [JsonPropertyName("protection_enabled")]
    public bool ProtectionEnabled { get; set; }

    [JsonPropertyName("running")]
    public bool Running { get; set; }
}