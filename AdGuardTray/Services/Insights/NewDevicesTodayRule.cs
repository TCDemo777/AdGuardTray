using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services.Insights;

public sealed class NewDevicesTodayRule : IInsightRule
{
    public Task<IEnumerable<Insight>> EvaluateAsync(
        InsightContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeviceHistoryRecord[] devices = context.DeviceHistory
            .Where(record =>
                record.FirstSeen.ToLocalTime().Date ==
                context.EvaluatedAt.ToLocalTime().Date)
            .ToArray();

        if (devices.Length == 0)
            return Task.FromResult<IEnumerable<Insight>>([]);

        string noun = devices.Length == 1 ? "device" : "devices";
        return Task.FromResult<IEnumerable<Insight>>([new Insight
        {
            Title = "New devices",
            Description = $"{devices.Length} new {noun} joined today.",
            Severity = InsightSeverity.Information,
            Category = InsightCategory.Device,
            Timestamp = devices.Max(record => record.FirstSeen),
            ActionLabel = "Open Clients",
            Action = InsightActionKind.OpenClients
        }]);
    }
}
