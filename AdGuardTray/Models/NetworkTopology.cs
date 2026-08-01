namespace AdGuardTray.Models;

public enum NetworkMapNodeType
{
    Internet,
    Router,
    Network,
    Device
}

public sealed class NetworkMapNode
{
    public required string Id { get; init; }
    public NetworkMapNodeType Type { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string DeviceType { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public string MacAddress { get; init; } = string.Empty;
    public DateTimeOffset? LastSeen { get; init; }
    public string TypicalOnlinePeriod { get; init; } = string.Empty;
    public IReadOnlyList<NetworkMapNode> Children { get; init; } =
        Array.Empty<NetworkMapNode>();

    public string LastSeenDisplay => LastSeen?.ToLocalTime().ToString("g") ?? "Unavailable";
    public int DeviceCount => Children.Count;
}

public sealed record NetworkMapEdge(string FromNodeId, string ToNodeId);

public sealed class NetworkTopology
{
    public DateTimeOffset GeneratedAt { get; init; }
    public required NetworkMapNode Internet { get; init; }
    public required NetworkMapNode Router { get; init; }
    public IReadOnlyList<NetworkMapNode> Networks { get; init; } =
        Array.Empty<NetworkMapNode>();
    public IReadOnlyList<NetworkMapEdge> Edges { get; init; } =
        Array.Empty<NetworkMapEdge>();

    public static NetworkTopology Empty { get; } = new()
    {
        Internet = new NetworkMapNode { Id = "internet", Type = NetworkMapNodeType.Internet, Name = "Internet", Status = "Unknown" },
        Router = new NetworkMapNode { Id = "router", Type = NetworkMapNodeType.Router, Name = "RouterPilot Router", Status = "Unknown" }
    };
}
