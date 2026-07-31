using System;

namespace AdGuardTray.Models
{
    public class ClientActivityItem
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string EventType { get; set; } = "Update";
        public string Summary { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;

        public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
    }
}
