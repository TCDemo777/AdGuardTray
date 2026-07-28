using System;
using AdGuardTray.Models;

namespace AdGuardTray.Services
{
    public static class ProtectionStateNotifier
    {
        public static event EventHandler<AdGuardProtectionStatus>? StateChanged;

        public static void Publish(
            AdGuardProtectionStatus status)
        {
            if (status is null)
            {
                return;
            }

            StateChanged?.Invoke(
                null,
                status);
        }
    }
}
