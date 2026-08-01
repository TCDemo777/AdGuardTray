using System;

namespace AdGuardTray.Models
{
    public class AdGuardTimePoint
    {
        public DateTime Timestamp { get; set; }

        public int Queries { get; set; }

        public int Blocked { get; set; }

        public string FormatTimeLabel(string timeUnits)
        {
            return timeUnits.ToLowerInvariant() switch
            {
                "second" or "seconds" => Timestamp.ToString("HH:mm:ss"),
                "minute" or "minutes" => Timestamp.ToString("HH:mm"),
                "day" or "days" => Timestamp.ToString("dd MMM"),
                "month" or "months" => Timestamp.ToString("MMM yyyy"),
                _ => Timestamp.ToString("HH:mm")
            };
        }
    }
}
