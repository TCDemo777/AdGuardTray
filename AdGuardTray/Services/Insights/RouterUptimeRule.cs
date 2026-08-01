using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services.Insights;

public sealed class RouterUptimeRule : IInsightRule
{
    private static readonly Regex DaysPattern =
        new(@"(?<days>\d+)\s+days?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task<IEnumerable<Insight>> EvaluateAsync(
        InsightContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Match match = DaysPattern.Match(context.RouterHealth.Uptime);

        if (!match.Success ||
            !int.TryParse(match.Groups["days"].Value, out int days) ||
            days <= 14)
        {
            return Task.FromResult<IEnumerable<Insight>>([]);
        }

        return Task.FromResult<IEnumerable<Insight>>([new Insight
        {
            Title = "Router uptime",
            Description = "Router uptime exceeds 14 days.",
            Severity = InsightSeverity.Information,
            Category = InsightCategory.Router,
            Timestamp = context.EvaluatedAt,
            ActionLabel = "Reboot Router",
            Action = InsightActionKind.RebootRouter
        }]);
    }
}
