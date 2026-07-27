namespace AdGuardTray.Models
{
    public class AppSettings
    {
        public string RouterIp { get; set; } = "192.168.1.1";

        public string Username { get; set; } = "root";

        public string EncryptedPassword { get; set; } = "";

        public bool RememberPassword { get; set; } = true;

        public bool StartWithWindows { get; set; } = false;

        public int RefreshIntervalSeconds { get; set; } = 30;

        public int DefaultPauseMinutes { get; set; } = 30;
    }
}
