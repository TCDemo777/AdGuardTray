using System;
using AdGuardTray.Configuration;

namespace AdGuardTray.Services;

public sealed class RouterEndpointProvider
{
    public RouterEndpointProvider(RouterConnectionOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Options.Validate();
    }

    public RouterConnectionOptions Options { get; }

    public Uri RouterBaseUri => Options.RouterBaseUri;
    public Uri RouterRpcUri => Options.RouterRpcUri;
    public Uri AdGuardBaseUri => Options.AdGuardBaseUri;

    public Uri AdGuardControl(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return new Uri(
            Options.AdGuardControlBaseUri,
            relativePath.TrimStart('/'));
    }
}
