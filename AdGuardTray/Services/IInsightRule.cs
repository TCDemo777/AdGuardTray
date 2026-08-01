using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services;

public interface IInsightRule
{
    Task<IEnumerable<Insight>> EvaluateAsync(
        InsightContext context,
        CancellationToken cancellationToken);
}
