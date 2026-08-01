using System;

namespace AdGuardTray.Models
{
    public class AdGuardTimePoint
    {
        public DateTime Timestamp { get; set; }

        public int Queries { get; set; }

        public int Blocked { get; set; }

        public string TimeLabel =>
            Timestamp.ToString("HH:mm");
    }
}