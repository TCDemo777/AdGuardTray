using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services.Insights;

public sealed class HighMemoryRule : IInsightRule
{
    public Task<IEnumerable<Insight>> EvaluateAsync(
        InsightContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.MemoryPercentage <= 90)
            return Task.FromResult<IEnumerable<Insight>>([]);

        return Task.FromResult<IEnumerable<Insight>>([new Insight
        {
            Title = "High memory usage",
            Description = "Router memory usage is above 90%.",
            Severity = InsightSeverity.Warning,
            Category = InsightCategory.System,
            Timestamp = context.EvaluatedAt,
            ActionLabel = "View Analytics",
            Action = InsightActionKind.ViewAnalytics
        }]);
    }
}
