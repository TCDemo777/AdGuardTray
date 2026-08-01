using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services.Insights;

public sealed class HighCpuRule : IInsightRule
{
    public Task<IEnumerable<Insight>> EvaluateAsync(
        InsightContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.CpuPercentage <= 90)
            return Task.FromResult<IEnumerable<Insight>>([]);

        return Task.FromResult<IEnumerable<Insight>>([new Insight
        {
            Title = "High CPU usage",
            Description = "Router CPU usage is above 90%.",
            Severity = InsightSeverity.Warning,
            Category = InsightCategory.System,
            Timestamp = context.EvaluatedAt,
            ActionLabel = "View Analytics",
            Action = InsightActionKind.ViewAnalytics
        }]);
    }
}
