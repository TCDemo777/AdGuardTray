using System;
using RouterPilot.Models;

namespace RouterPilot.Services
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
