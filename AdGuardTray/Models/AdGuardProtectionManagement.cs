using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdGuardTray.Models
{
    public sealed class AdGuardProtectionOptions
    {
        public bool FilteringEnabled { get; set; }
        public int FilteringIntervalHours { get; set; }
        public bool SafeBrowsingEnabled { get; set; }
        public bool ParentalEnabled { get; set; }
        public bool SafeSearchEnabled { get; set; }
        public bool QueryLogEnabled { get; set; }
        public bool QueryLogAnonymizeClientIp { get; set; }
        public double QueryLogInterval { get; set; }
        public string[] QueryLogIgnored { get; set; } = [];
        public AdGuardSafeSearchSettings SafeSearch { get; set; } = new();
    }

    public sealed class AdGuardSafeSearchSettings
    {
        public bool Enabled { get; set; }
        public bool Bing { get; set; } = true;
        public bool DuckDuckGo { get; set; } = true;
        public bool Ecosia { get; set; } = true;
        public bool Google { get; set; } = true;
        public bool Pixabay { get; set; } = true;
        public bool Yandex { get; set; } = true;
        public bool YouTube { get; set; } = true;
    }

    public sealed class AdGuardBlockedServicesConfig
    {
        public string ScheduleJson { get; set; } = "{}";
        public HashSet<string> EnabledIds { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class BlockedServiceItem : ObservableObject
    {
        private bool _isBlocked;
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public bool IsBlocked
        {
            get => _isBlocked;
            set => SetProperty(ref _isBlocked, value);
        }
    }

    public sealed class DnsRewriteRule
    {
        public string Domain { get; init; } = "";
        public string Answer { get; init; } = "";
        public string Display => $"{Domain}  →  {Answer}";
    }

    public sealed class CustomFilteringRule
    {
        public string Rule { get; init; } = "";
        public string Type { get; init; } = "Custom";
        public string Display => $"[{Type}] {Rule}";
    }
}
