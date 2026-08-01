using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services;

public sealed class InsightEngine
{
    private readonly IReadOnlyList<IInsightRule> _rules;

    public InsightEngine(IEnumerable<IInsightRule> rules)
    {
        _rules = rules.ToArray();
    }

    public async Task<IReadOnlyList<Insight>> EvaluateAsync(
        InsightContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var insights = new List<Insight>();

        foreach (IInsightRule rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                IEnumerable<Insight> results =
                    await rule.EvaluateAsync(context, cancellationToken)
                        .ConfigureAwait(false);
                insights.AddRange(results);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Insight rule {rule.GetType().Name} failed: {ex}");
            }
        }

        return insights
            .OrderBy(insight => insight.Severity switch
            {
                InsightSeverity.Critical => 0,
                InsightSeverity.Warning => 1,
                _ => 2
            })
            .ThenByDescending(insight => insight.Timestamp)
            .ToArray();
    }
}
