using AdGuardTray.Models;
using Microsoft.Data.Sqlite;

namespace AdGuardTray.Services;

public sealed class HistoryRepository
{
    private readonly IDataStore _dataStore;

    public HistoryRepository(IDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public async Task<int> GetSchemaVersionAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await _dataStore.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Version FROM SchemaVersion WHERE Id = 1;";

        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null || value is DBNull ? 0 : Convert.ToInt32(value);
    }

    public async Task AddEventAsync(
        DeviceConnectionEvent connectionEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connectionEvent);
        string normalizedMac = DeviceHistoryService.NormalizeMacAddress(
            connectionEvent.MacAddress);
        if (normalizedMac.Length != 12)
            throw new ArgumentException("A valid MAC address is required.", nameof(connectionEvent));

        await using var connection =
            await _dataStore.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO DeviceConnections
                (MacAddress, TimestampUtc, EventType, IpAddress,
                 NetworkName, Hostname, FriendlyName)
            VALUES
                ($macAddress, $timestampUtc, $eventType, $ipAddress,
                 $networkName, $hostname, $friendlyName);
            """;
        AddEventParameters(command, connectionEvent, normalizedMac);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceConnectionEvent>>
        GetRecentEventsByMacAsync(
            string macAddress,
            int maximumCount = 20,
            CancellationToken cancellationToken = default)
    {
        string normalized = DeviceHistoryService.NormalizeMacAddress(macAddress);
        if (normalized.Length != 12 || maximumCount <= 0)
            return Array.Empty<DeviceConnectionEvent>();

        await using var connection =
            await _dataStore.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, MacAddress, TimestampUtc, EventType, IpAddress,
                   NetworkName, Hostname, FriendlyName
            FROM DeviceConnections
            WHERE MacAddress = $macAddress
            ORDER BY TimestampUtc DESC, Id DESC
            LIMIT $maximumCount;
            """;
        command.Parameters.AddWithValue("$macAddress", normalized);
        command.Parameters.AddWithValue("$maximumCount", maximumCount);
        return await ReadEventsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceConnectionEvent>> GetEventsBetweenAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default)
    {
        if (endUtc < startUtc)
            throw new ArgumentOutOfRangeException(nameof(endUtc));

        await using var connection =
            await _dataStore.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, MacAddress, TimestampUtc, EventType, IpAddress,
                   NetworkName, Hostname, FriendlyName
            FROM DeviceConnections
            WHERE TimestampUtc >= $startUtc AND TimestampUtc <= $endUtc
            ORDER BY TimestampUtc DESC, Id DESC;
            """;
        command.Parameters.AddWithValue("$startUtc", startUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$endUtc", endUtc.ToUniversalTime().ToString("O"));
        return await ReadEventsAsync(command, cancellationToken);
    }

    private static void AddEventParameters(
        SqliteCommand command,
        DeviceConnectionEvent connectionEvent,
        string normalizedMac)
    {
        command.Parameters.AddWithValue("$macAddress", normalizedMac);
        command.Parameters.AddWithValue(
            "$timestampUtc",
            connectionEvent.TimestampUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$eventType", (int)connectionEvent.EventType);
        command.Parameters.AddWithValue("$ipAddress", DbValue(connectionEvent.IpAddress));
        command.Parameters.AddWithValue("$networkName", DbValue(connectionEvent.NetworkName));
        command.Parameters.AddWithValue("$hostname", DbValue(connectionEvent.Hostname));
        command.Parameters.AddWithValue("$friendlyName", DbValue(connectionEvent.FriendlyName));
    }

    private static async Task<IReadOnlyList<DeviceConnectionEvent>> ReadEventsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var events = new List<DeviceConnectionEvent>();
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new DeviceConnectionEvent
            {
                Id = reader.GetInt64(0),
                MacAddress = reader.GetString(1),
                TimestampUtc = DateTimeOffset.Parse(reader.GetString(2)),
                EventType = (DeviceConnectionEventType)reader.GetInt32(3),
                IpAddress = GetString(reader, 4),
                NetworkName = GetString(reader, 5),
                Hostname = GetString(reader, 6),
                FriendlyName = GetString(reader, 7)
            });
        }

        return events;
    }

    private static object DbValue(string value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static string GetString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
}
