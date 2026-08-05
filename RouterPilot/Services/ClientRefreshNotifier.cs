using System;

namespace RouterPilot.Services
{
    public static class ClientRefreshNotifier
    {
        public static event EventHandler? RefreshRequested;

        public static void RequestRefresh()
        {
            RefreshRequested?.Invoke(
                null,
                EventArgs.Empty);
        }
    }
}
