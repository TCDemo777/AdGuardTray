using System;
using System.Collections.Generic;

namespace AdGuardTray.Models;

public sealed class DeviceHistoryRecord
{
    public string MacAddress { get; set; } = string.Empty;

    public string FriendlyName { get; set; } = string.Empty;

    public string Hostname { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public string DeviceType { get; set; } = string.Empty;

    public DateTimeOffset FirstSeen { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    public string LastIpAddress { get; set; } = string.Empty;

    public string LastNetworkName { get; set; } = string.Empty;

    public string LastSsid { get; set; } = string.Empty;

    public bool IsCurrentlyOnline { get; set; }

    public long TimesSeenOnline { get; set; }

    public long TimesConnected { get; set; }

    public List<string> PreviousIpAddresses { get; set; } = new();

    public List<string> PreviousNetworkNames { get; set; } = new();
}
