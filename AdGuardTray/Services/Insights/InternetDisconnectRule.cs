using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services.Insights;

public sealed class InternetDisconnectRule : IInsightRule
{
    public Task<IEnumerable<Insight>> EvaluateAsync(
        InsightContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset cutoff = context.EvaluatedAt.AddHours(-24);
        AppNotification? interruption = context.NotificationHistory
            .Where(notification =>
                notification.Timestamp >= cutoff &&
                (notification.Category == NotificationCategory.Internet ||
                 notification.DeduplicationKey == "RouterOffline"))
            .OrderByDescending(notification => notification.Timestamp)
            .FirstOrDefault();

        if (interruption is null)
            return Task.FromResult<IEnumerable<Insight>>([]);

        return Task.FromResult<IEnumerable<Insight>>([new Insight
        {
            Title = "Internet interruption",
            Description = "Internet connection was interrupted during the last 24 hours.",
            Severity = InsightSeverity.Warning,
            Category = InsightCategory.Internet,
            Timestamp = interruption.Timestamp,
            ActionLabel = "View Notifications",
            Action = InsightActionKind.ViewNotifications
        }]);
    }
}
