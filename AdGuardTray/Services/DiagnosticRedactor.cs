using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AdGuardTray.Services;

public sealed partial class DiagnosticRedactor
{
    public const string RedactedValue = "***REDACTED***";
    private readonly string[] _knownSecrets;

    public DiagnosticRedactor(SettingsService settingsService)
    {
        var settings = settingsService.Load();
        string decrypted = settingsService.DecryptPassword(settings.EncryptedPassword);
        _knownSecrets = new[] { settings.EncryptedPassword, decrypted }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(value => value.Length)
            .ToArray();
    }

    public bool IsSensitiveName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        SensitiveNameRegex().IsMatch(name);

    public string RedactText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        string result = text;
        foreach (string secret in _knownSecrets)
            result = result.Replace(secret, RedactedValue, StringComparison.Ordinal);

        result = AuthorizationRegex().Replace(result, "$1: " + RedactedValue);
        result = TokenRegex().Replace(result, "$1 " + RedactedValue);
        result = CookieRegex().Replace(result, "$1: " + RedactedValue);
        result = UrlUserInfoRegex().Replace(result, "${scheme}" + RedactedValue + "@");
        return result;
    }

    public string RedactDeviceIdentifiers(
        string? text,
        IEnumerable<AdGuardTray.Models.DeviceHistoryRecord> devices)
    {
        string result = RedactText(text);
        IEnumerable<string> identifiers = devices.SelectMany(device => new[]
        {
            device.MacAddress,
            device.LastIpAddress,
            device.Hostname,
            device.FriendlyName,
            device.LastSsid,
            device.LastNetworkName
        }).Where(value => !string.IsNullOrWhiteSpace(value) && value.Length >= 3)
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .OrderByDescending(value => value.Length);

        foreach (string identifier in identifiers)
            result = result.Replace(identifier, RedactedValue,
                StringComparison.OrdinalIgnoreCase);
        return MacAddressRegex().Replace(
            IpAddressRegex().Replace(result, RedactedValue), RedactedValue);
    }

    public Dictionary<string, object?> RedactObject(object source)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (PropertyInfo property in source.GetType().GetProperties())
        {
            if (!property.CanRead || property.GetIndexParameters().Length != 0)
                continue;

            object? value = property.GetValue(source);
            result[property.Name] = IsSensitiveName(property.Name)
                ? RedactedValue
                : RedactValue(value);
        }
        return result;
    }

    private object? RedactValue(object? value) => value switch
    {
        null => null,
        string text => RedactText(text),
        IEnumerable values when value is not string =>
            values.Cast<object?>().Select(RedactValue).ToArray(),
        _ => value
    };

    [GeneratedRegex("password|secret|token|key|credential|auth|cookie|session", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveNameRegex();

    [GeneratedRegex("(?im)^(Authorization)\\s*:\\s*[^\\r\\n]+")]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex("(?i)\\b(Bearer|Basic)\\s+[^\\s,;]+")]
    private static partial Regex TokenRegex();

    [GeneratedRegex("(?im)^((?:Set-)?Cookie)\\s*:\\s*[^\\r\\n]+")]
    private static partial Regex CookieRegex();

    [GeneratedRegex("(?<scheme>https?://)[^/@\\s]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlUserInfoRegex();

    [GeneratedRegex("\\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\\b")]
    private static partial Regex MacAddressRegex();

    [GeneratedRegex("\\b(?:\\d{1,3}\\.){3}\\d{1,3}\\b")]
    private static partial Regex IpAddressRegex();
}
