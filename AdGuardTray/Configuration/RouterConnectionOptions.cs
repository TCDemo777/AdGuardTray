using System;
using System.Net;
using System.Linq;

namespace AdGuardTray.Configuration;

public sealed class RouterConnectionOptions
{
    public string Host { get; set; } = string.Empty;
    public string RouterScheme { get; set; } = Uri.UriSchemeHttp;
    public int RouterPort { get; set; } = 80;
    public string AdGuardScheme { get; set; } = Uri.UriSchemeHttp;
    public int AdGuardPort { get; set; } = 3000;
    public int RequestTimeoutSeconds { get; set; } = 10;

    public void Validate()
    {
        Host = NormaliseHost(Host);

        if (string.IsNullOrWhiteSpace(Host))
            throw new InvalidOperationException("A router host must be configured.");

        if (!IPAddress.TryParse(Host, out _) &&
            !Uri.CheckHostName(Host).Equals(UriHostNameType.Dns))
        {
            throw new InvalidOperationException(
                $"'{Host}' is not a valid IP address or DNS host name.");
        }

        ValidateScheme(RouterScheme, nameof(RouterScheme));
        ValidateScheme(AdGuardScheme, nameof(AdGuardScheme));
        ValidatePort(RouterPort, nameof(RouterPort));
        ValidatePort(AdGuardPort, nameof(AdGuardPort));

        if (RequestTimeoutSeconds is < 1 or > 120)
            throw new InvalidOperationException(
                "RequestTimeoutSeconds must be between 1 and 120.");
    }

    public Uri RouterBaseUri => BuildUri(RouterScheme, RouterPort);
    public Uri RouterRpcUri => new(RouterBaseUri, "rpc");
    public Uri AdGuardBaseUri => BuildUri(AdGuardScheme, AdGuardPort);
    public Uri AdGuardControlBaseUri => new(AdGuardBaseUri, "control/");

    public static string NormaliseHost(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return string.Empty;

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return uri.Host;

        int slash = text.IndexOf('/');
        if (slash >= 0)
            text = text[..slash];

        if (text.StartsWith("[", StringComparison.Ordinal) &&
            text.EndsWith("]", StringComparison.Ordinal))
            return text[1..^1];

        int colonCount = text.Count(c => c == ':');
        if (colonCount == 1)
            text = text[..text.LastIndexOf(':')];

        return text.Trim();
    }

    private Uri BuildUri(string scheme, int port) =>
        new UriBuilder(scheme, Host, port, "/").Uri;

    private static void ValidateScheme(string scheme, string name)
    {
        if (!string.Equals(scheme, Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scheme, Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{name} must be either http or https.");
        }
    }

    private static void ValidatePort(int port, string name)
    {
        if (port is < 1 or > 65535)
            throw new InvalidOperationException(
                $"{name} must be between 1 and 65535.");
    }
}
