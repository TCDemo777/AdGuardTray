using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services.Insights;

public sealed class AdGuardDisabledRule : IInsightRule
{
    public Task<IEnumerable<Insight>> EvaluateAsync(
        InsightContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.AdGuardProtectionStatusKnown ||
            context.AdGuardProtectionEnabled)
        {
            return Task.FromResult<IEnumerable<Insight>>([]);
        }

        return Task.FromResult<IEnumerable<Insight>>([new Insight
        {
            Title = "AdGuard protection",
            Description = "AdGuard protection is currently disabled.",
            Severity = InsightSeverity.Warning,
            Category = InsightCategory.AdGuard,
            Timestamp = context.EvaluatedAt,
            ActionLabel = "Enable Protection",
            Action = InsightActionKind.EnableProtection
        }]);
    }
}
