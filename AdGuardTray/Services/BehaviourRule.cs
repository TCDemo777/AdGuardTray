using AdGuardTray.Models;

namespace AdGuardTray.Services;

public abstract class BehaviourRule
{
    public abstract Task<IEnumerable<BehaviourObservation>> EvaluateAsync(
        BehaviourAnalysis analysis,
        CancellationToken cancellationToken);
}
