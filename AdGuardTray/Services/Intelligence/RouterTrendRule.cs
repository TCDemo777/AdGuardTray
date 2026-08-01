using AdGuardTray.Models;

namespace AdGuardTray.Services.Intelligence;

public sealed class RouterTrendRule : BehaviourRule
{
    public override Task<IEnumerable<BehaviourObservation>> EvaluateAsync(
        BehaviourAnalysis analysis,
        CancellationToken cancellationToken)
    {
        RouterHealthMinuteSnapshot[] evening = analysis.RouterHealth
            .Where(item => item.TimestampUtc.ToLocalTime().Hour is >= 17 and <= 23 &&
                           item.AverageCpuUsagePercent.HasValue)
            .OrderBy(item => item.TimestampUtc).ToArray();
        if (evening.Length < 20)
            return Task.FromResult<IEnumerable<BehaviourObservation>>(Array.Empty<BehaviourObservation>());
        int half = evening.Length / 2;
        double earlier = evening.Take(half).Average(item => item.AverageCpuUsagePercent!.Value);
        double later = evening.Skip(half).Average(item => item.AverageCpuUsagePercent!.Value);
        if (later - earlier < 10)
            return Task.FromResult<IEnumerable<BehaviourObservation>>(Array.Empty<BehaviourObservation>());
        return Task.FromResult<IEnumerable<BehaviourObservation>>(new[]
        {
            new BehaviourObservation
            {
                Title = "Evening CPU trend",
                Description = "CPU usage has steadily increased during evenings.",
                Category = "Router",
                Severity = BehaviourObservationSeverity.Warning,
                Priority = 75
            }
        });
    }
}
