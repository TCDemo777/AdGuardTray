namespace AdGuardTray.Models;

public sealed class AppSettings
{
    // Deliberately empty: first-run setup must collect this.
    public string RouterHost { get; set; } = string.Empty;

    // Kept temporarily for migration from existing settings.json files.
    public string? RouterIp { get; set; }

    public int RouterPort { get; set; } = 80;
    public int AdGuardPort { get; set; } = 3000;
    public bool UseRouterHttps { get; set; }
    public bool UseAdGuardHttps { get; set; }

    public string Username { get; set; } = "root";
    public string EncryptedPassword { get; set; } = string.Empty;
    public bool RememberPassword { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public string Theme { get; set; } = "System";
    public int RefreshIntervalSeconds { get; set; } = 30;
    public int DefaultPauseMinutes { get; set; } = 30;
    public DateTimeOffset? LastSuccessfulUpdateCheckUtc { get; set; }
    public string LatestVersionSeen { get; set; } = string.Empty;
    public string LastNotifiedUpdateVersion { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(RouterHost);
}
