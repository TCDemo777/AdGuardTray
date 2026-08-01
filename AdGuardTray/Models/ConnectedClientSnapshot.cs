using System;
using System.Collections.Generic;

namespace AdGuardTray.Models;

public sealed class ConnectedClientSnapshot
{
    public ConnectedClientSnapshot(
        IReadOnlyList<ClientInfo> clients,
        bool isComplete)
    {
        Clients = clients ?? throw new ArgumentNullException(nameof(clients));
        IsComplete = isComplete;
    }

    public IReadOnlyList<ClientInfo> Clients { get; }

    public bool IsComplete { get; }
}
