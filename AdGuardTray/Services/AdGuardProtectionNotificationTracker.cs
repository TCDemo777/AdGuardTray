using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services;

public enum ProtectionStateSource
{
    Refresh,
    ManualAction
}

public sealed class AdGuardProtectionNotificationTracker
{
    private readonly NotificationService _notificationService;
    private readonly object _syncRoot = new();
    private bool? _lastConfirmedState;

    public AdGuardProtectionNotificationTracker(
        NotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task ProcessProtectionStateAsync(
        bool isEnabled,
        ProtectionStateSource source)
    {
        bool shouldNotify;

        lock (_syncRoot)
        {
            if (_lastConfirmedState is null)
            {
                _lastConfirmedState = isEnabled;
                shouldNotify = source == ProtectionStateSource.ManualAction;
            }
            else if (_lastConfirmedState == isEnabled)
            {
                return;
            }
            else
            {
                _lastConfirmedState = isEnabled;
                shouldNotify = true;
            }
        }

        if (!shouldNotify)
            return;

        await _notificationService.AddAsync(new AppNotification
        {
            Title = isEnabled
                ? "AdGuard Protection Enabled"
                : "AdGuard Protection Disabled",
            Message = isEnabled
                ? "AdGuard Home DNS protection has been enabled."
                : "AdGuard Home DNS protection has been disabled.",
            Severity = isEnabled
                ? NotificationSeverity.Success
                : NotificationSeverity.Warning,
            Category = NotificationCategory.AdGuard,
            DeduplicationKey = isEnabled
                ? "AdGuardProtectionEnabled"
                : "AdGuardProtectionDisabled"
        });
    }
}
