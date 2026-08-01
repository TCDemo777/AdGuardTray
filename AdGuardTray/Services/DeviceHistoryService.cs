using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AdGuardTray.Models;

namespace AdGuardTray.Services;

public sealed class DeviceHistoryService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _storeFile;
    private readonly HistoryRepository _historyRepository;
    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly Dictionary<string, DeviceHistoryRecord> _records =
        new(StringComparer.OrdinalIgnoreCase);
    private Task? _disposeTask;
    private bool _disposalStarted;
    private bool _hasCompleteSnapshot;

    public DeviceHistoryService(
        HistoryRepository historyRepository,
        string? dataFolder = null)
    {
        _historyRepository = historyRepository;
        string folder = dataFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AdGuardTray");
        _storeFile = Path.Combine(folder, "device-history.json");
    }

    public IReadOnlyCollection<DeviceHistoryRecord> Records
    {
        get
        {
            lock (_records)
            {
                return _records.Values
                    .OrderByDescending(record => record.LastSeen)
                    .Select(CloneRecord)
                    .ToArray();
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        List<DeviceHistoryRecord> loaded = await LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        lock (_records)
        {
            _records.Clear();

            foreach (DeviceHistoryRecord record in loaded)
            {
                string mac = NormalizeMacAddress(record.MacAddress);
                if (mac.Length != 12)
                    continue;

                record.MacAddress = mac;
                record.PreviousIpAddresses ??= new List<string>();
                record.PreviousNetworkNames ??= new List<string>();
                _records[mac] = record;
            }

            _hasCompleteSnapshot = _records.Count > 0;
        }
    }

    public bool HasCompleteSnapshot
    {
        get
        {
            lock (_records)
                return _hasCompleteSnapshot;
        }
    }

    public bool HasSeenDevice(string? macAddress)
    {
        string normalized = NormalizeMacAddress(macAddress);
        if (normalized.Length != 12)
            return false;

        lock (_records)
            return _records.ContainsKey(normalized);
    }

    public DeviceHistoryRecord? GetByMacAddress(string? macAddress)
    {
        string normalized = NormalizeMacAddress(macAddress);
        if (normalized.Length != 12)
            return null;

        lock (_records)
        {
            return _records.TryGetValue(
                normalized,
                out DeviceHistoryRecord? record)
                ? CloneRecord(record)
                : null;
        }
    }

    public async Task UpdateFromConnectedClientsAsync(
        ConnectedClientSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.IsComplete)
            return;

        Dictionary<string, ClientInfo> connected = snapshot.Clients
            .Select(client =>
                (Mac: NormalizeMacAddress(client.MacAddress), Client: client))
            .Where(item => item.Mac.Length == 12)
            .GroupBy(item => item.Mac, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Client,
                StringComparer.OrdinalIgnoreCase);
        HashSet<string> onlineMacAddresses = connected.Keys.ToHashSet(
            StringComparer.OrdinalIgnoreCase);

        lock (_lifecycleLock)
        {
            if (_disposalStarted)
                return;
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_lifecycleLock)
            {
                if (_disposalStarted)
                    return;
            }

            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            var connectionEvents = new List<DeviceConnectionEvent>();

            lock (_records)
            {
                foreach (DeviceHistoryRecord record in _records.Values)
                {
                    if (record.IsCurrentlyOnline &&
                        !onlineMacAddresses.Contains(record.MacAddress))
                    {
                        record.IsCurrentlyOnline = false;
                        connectionEvents.Add(CreateEvent(
                            record,
                            observedAt,
                            DeviceConnectionEventType.Disconnected));
                    }
                }

                foreach ((string mac, ClientInfo client) in connected)
                {
                    bool isNew = false;
                    bool reconnected = false;
                    string previousIpAddress = string.Empty;
                    string previousNetworkName = string.Empty;

                    if (!_records.TryGetValue(mac, out DeviceHistoryRecord? record))
                    {
                        isNew = true;
                        record = new DeviceHistoryRecord
                        {
                            MacAddress = mac,
                            FirstSeen = observedAt,
                            TimesConnected = 1
                        };
                        _records.Add(mac, record);
                    }
                    else if (!record.IsCurrentlyOnline)
                    {
                        reconnected = true;
                        record.TimesConnected++;
                    }

                    previousIpAddress = record.LastIpAddress;
                    previousNetworkName = HasUsefulValue(record.LastSsid)
                        ? record.LastSsid
                        : record.LastNetworkName;

                    record.LastSeen = observedAt;
                    record.IsCurrentlyOnline = true;
                    record.TimesSeenOnline++;
                    UpdateIdentity(record, client);
                    record.LastIpAddress = UpdateLastValue(
                        record.LastIpAddress,
                        client.IpAddress,
                        record.PreviousIpAddresses);
                    record.LastNetworkName = UpdateLastValue(
                        record.LastNetworkName,
                        client.ConnectionType,
                        record.PreviousNetworkNames);

                    if (HasUsefulValue(client.WifiNetwork))
                        record.LastSsid = client.WifiNetwork.Trim();

                    string currentNetworkName = HasUsefulValue(client.WifiNetwork)
                        ? client.WifiNetwork.Trim()
                        : HasUsefulValue(client.ConnectionType)
                            ? client.ConnectionType.Trim()
                            : string.Empty;

                    if (isNew)
                    {
                        connectionEvents.Add(CreateEvent(
                            record,
                            observedAt,
                            DeviceConnectionEventType.FirstSeen));
                    }
                    else if (reconnected)
                    {
                        connectionEvents.Add(CreateEvent(
                            record,
                            observedAt,
                            DeviceConnectionEventType.Connected));
                    }

                    if (HasChanged(previousIpAddress, client.IpAddress))
                    {
                        connectionEvents.Add(CreateEvent(
                            record,
                            observedAt,
                            DeviceConnectionEventType.IpChanged,
                            $"{previousIpAddress} → {client.IpAddress!.Trim()}"));
                    }

                    if (HasChanged(previousNetworkName, currentNetworkName))
                    {
                        connectionEvents.Add(CreateEvent(
                            record,
                            observedAt,
                            DeviceConnectionEventType.NetworkChanged,
                            networkName:
                                $"{previousNetworkName} → {currentNetworkName}"));
                    }
                }

                _hasCompleteSnapshot = true;
            }

            foreach (DeviceConnectionEvent connectionEvent in connectionEvents)
            {
                try
                {
                    await _historyRepository.AddEventAsync(
                            connectionEvent,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Debug.WriteLine(
                        $"Unable to save device connection event: {ex}");
                }
            }

            try
            {
                await SaveCurrentStateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException
                                           or UnauthorizedAccessException
                                           or JsonException)
            {
                Debug.WriteLine($"Unable to save device history: {ex}");
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleLock)
        {
            return _disposeTask ?? FlushCoreAsync(cancellationToken);
        }
    }

    private async Task FlushCoreAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveCurrentStateAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<List<DeviceHistoryRecord>> LoadAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_storeFile))
                return new List<DeviceHistoryRecord>();

            await using var stream = new FileStream(
                _storeFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                useAsync: true);

            return await JsonSerializer.DeserializeAsync<List<DeviceHistoryRecord>>(
                       stream,
                       JsonOptions,
                       cancellationToken)
                       .ConfigureAwait(false) ?? new List<DeviceHistoryRecord>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or JsonException)
        {
            Debug.WriteLine($"Unable to load device history: {ex}");
            return new List<DeviceHistoryRecord>();
        }
    }

    private async Task SaveCurrentStateAsync(CancellationToken cancellationToken)
    {
        List<DeviceHistoryRecord> snapshot;
        lock (_records)
        {
            snapshot = _records.Values
                .OrderBy(record => record.MacAddress)
                .Select(CloneRecord)
                .ToList();
        }

        string folder = Path.GetDirectoryName(_storeFile)!;
        Directory.CreateDirectory(folder);
        string temporaryFile = _storeFile + ".tmp";
        string backupFile = _storeFile + ".bak";

        await using (var stream = new FileStream(
                         temporaryFile,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         4096,
                         useAsync: true))
        {
            await JsonSerializer.SerializeAsync(
                    stream,
                    snapshot,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(_storeFile))
            File.Replace(temporaryFile, _storeFile, backupFile, true);
        else
            File.Move(temporaryFile, _storeFile);
    }

    private static void UpdateIdentity(
        DeviceHistoryRecord record,
        ClientInfo client)
    {
        if (HasUsefulValue(client.Name))
            record.FriendlyName = client.Name.Trim();
        if (HasUsefulValue(client.RouterName))
            record.Hostname = client.RouterName.Trim();
        if (HasUsefulValue(client.Manufacturer))
            record.Manufacturer = client.Manufacturer.Trim();
        if (HasUsefulValue(client.DeviceType))
            record.DeviceType = client.DeviceType.Trim();
    }

    private static string UpdateLastValue(
        string currentValue,
        string? incomingValue,
        ICollection<string> previousValues)
    {
        if (!HasUsefulValue(incomingValue))
            return currentValue;

        string incoming = incomingValue!.Trim();
        if (string.Equals(currentValue, incoming, StringComparison.OrdinalIgnoreCase))
            return currentValue;

        if (HasUsefulValue(currentValue) &&
            !previousValues.Contains(currentValue, StringComparer.OrdinalIgnoreCase))
        {
            previousValues.Add(currentValue);
        }

        return incoming;
    }

    private static bool HasUsefulValue(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value != "-" &&
        !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
        !value.StartsWith("Unknown ", StringComparison.OrdinalIgnoreCase);

    private static bool HasChanged(string? previous, string? current) =>
        HasUsefulValue(previous) &&
        HasUsefulValue(current) &&
        !string.Equals(
            previous!.Trim(),
            current!.Trim(),
            StringComparison.OrdinalIgnoreCase);

    private static DeviceConnectionEvent CreateEvent(
        DeviceHistoryRecord record,
        DateTimeOffset timestampUtc,
        DeviceConnectionEventType eventType,
        string? ipAddress = null,
        string? networkName = null) =>
        new()
        {
            MacAddress = record.MacAddress,
            TimestampUtc = timestampUtc.ToUniversalTime(),
            EventType = eventType,
            IpAddress = ipAddress ?? record.LastIpAddress,
            NetworkName = networkName ??
                (HasUsefulValue(record.LastSsid)
                    ? record.LastSsid
                    : record.LastNetworkName),
            Hostname = record.Hostname,
            FriendlyName = record.FriendlyName
        };

    public static string NormalizeMacAddress(string? value)
    {
        string normalized = new((value ?? string.Empty)
            .Where(char.IsAsciiHexDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        return normalized.Length == 12 ? normalized : string.Empty;
    }

    private static DeviceHistoryRecord CloneRecord(DeviceHistoryRecord record) =>
        new()
        {
            MacAddress = record.MacAddress,
            FriendlyName = record.FriendlyName,
            Hostname = record.Hostname,
            Manufacturer = record.Manufacturer,
            DeviceType = record.DeviceType,
            FirstSeen = record.FirstSeen,
            LastSeen = record.LastSeen,
            LastIpAddress = record.LastIpAddress,
            LastNetworkName = record.LastNetworkName,
            LastSsid = record.LastSsid,
            IsCurrentlyOnline = record.IsCurrentlyOnline,
            TimesSeenOnline = record.TimesSeenOnline,
            TimesConnected = record.TimesConnected,
            PreviousIpAddresses = new List<string>(record.PreviousIpAddresses),
            PreviousNetworkNames = new List<string>(record.PreviousNetworkNames)
        };

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            if (_disposeTask is not null)
                return new ValueTask(_disposeTask);

            _disposalStarted = true;
            _disposeTask = DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await FlushCoreAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
