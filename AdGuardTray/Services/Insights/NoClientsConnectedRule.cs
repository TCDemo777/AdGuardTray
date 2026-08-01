using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services.Insights;

public sealed class NoClientsConnectedRule : IInsightRule
{
    public Task<IEnumerable<Insight>> EvaluateAsync(
        InsightContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.ConnectedClientSnapshotComplete ||
            context.ConnectedClientMacAddresses.Count > 0)
        {
            return Task.FromResult<IEnumerable<Insight>>([]);
        }

        return Task.FromResult<IEnumerable<Insight>>([new Insight
        {
            Title = "Client connectivity",
            Description = "No client devices are currently connected.",
            Severity = InsightSeverity.Warning,
            Category = InsightCategory.Device,
            Timestamp = context.EvaluatedAt,
            ActionLabel = "Open Clients",
            Action = InsightActionKind.OpenClients
        }]);
    }
}
