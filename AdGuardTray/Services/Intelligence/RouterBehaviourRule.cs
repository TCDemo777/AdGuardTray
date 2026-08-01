using AdGuardTray.Models;

namespace AdGuardTray.Services.Intelligence;

public sealed class RouterBehaviourRule : BehaviourRule
{
    public override Task<IEnumerable<BehaviourObservation>> EvaluateAsync(
        BehaviourAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var results = new List<BehaviourObservation>();
        if (analysis.WanHistory.Count >= 60)
        {
            var busiest = analysis.WanHistory
                .GroupBy(item => item.TimestampUtc.ToLocalTime().Hour)
                .Select(group => new { Hour = group.Key,
                    Load = group.Average(item => item.AverageDownloadMbps + item.AverageUploadMbps) })
                .OrderByDescending(item => item.Load).First();
            results.Add(new BehaviourObservation
            {
                Title = "Busiest WAN hour",
                Description = $"WAN traffic is typically busiest between {busiest.Hour:00}:00 and {(busiest.Hour + 1) % 24:00}:00.",
                Category = "WAN", Priority = 35
            });
        }

        var network = analysis.DeviceEvents
            .Where(item => !string.IsNullOrWhiteSpace(item.NetworkName))
            .GroupBy(item => new { item.NetworkName, Hour = item.TimestampUtc.ToLocalTime().Hour })
            .OrderByDescending(group => group.Count()).FirstOrDefault();
        if (network is not null && network.Count() >= 3)
            results.Add(new BehaviourObservation
            {
                Title = "Busiest network period",
                Description = $"{network.Key.NetworkName} is busiest between {network.Key.Hour:00}:00 and {(network.Key.Hour + 1) % 24:00}:00.",
                Category = "Network", Priority = 40
            });

        var cpuHour = analysis.RouterHealth
            .Where(item => item.AverageCpuUsagePercent.HasValue)
            .GroupBy(item => item.TimestampUtc.ToLocalTime().Hour)
            .Select(group => new { Hour = group.Key,
                Average = group.Average(item => item.AverageCpuUsagePercent!.Value) })
            .OrderByDescending(item => item.Average).FirstOrDefault();
        var memoryHour = analysis.RouterHealth
            .Where(item => item.AverageMemoryUsagePercent.HasValue)
            .GroupBy(item => item.TimestampUtc.ToLocalTime().Hour)
            .Select(group => new { Hour = group.Key,
                Average = group.Average(item => item.AverageMemoryUsagePercent!.Value) })
            .OrderByDescending(item => item.Average).FirstOrDefault();
        if (cpuHour is not null && analysis.RouterHealth.Count >= 60)
            results.Add(new BehaviourObservation
            {
                Title = "Typical CPU peak",
                Description = $"CPU usage is highest around {cpuHour.Hour:00}:00, averaging {cpuHour.Average:F0}%.",
                Category = "Router", Priority = 30
            });
        if (memoryHour is not null && analysis.RouterHealth.Count >= 60)
            results.Add(new BehaviourObservation
            {
                Title = "Typical memory peak",
                Description = $"Memory usage is highest around {memoryHour.Hour:00}:00, averaging {memoryHour.Average:F0}%.",
                Category = "Router", Priority = 28
            });
        return Task.FromResult<IEnumerable<BehaviourObservation>>(results);
    }
}
