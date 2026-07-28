using System;

namespace AdGuardTray.Models
{
    public class AdGuardProtectionStatus
    {
        public bool IsEnabled { get; set; }

        public bool IsPaused { get; set; }

        public TimeSpan RemainingPause { get; set; }

        public string StateText =>
            IsEnabled
                ? "Protection enabled"
                : IsPaused
                    ? "Protection paused"
                    : "Protection disabled";
    }
}
