using AdGuardTray.Models;
using System.IO;
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

    public async Task<DatabaseHealthReport> GetDatabaseHealthAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataStore
            .OpenReadOnlyConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var tables = new List<DatabaseTableHealth>();
        var tableNames = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                tableNames.Add(reader.GetString(0));
        }

        var timestampColumns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DeviceHistory"] = "LastSeenUtc",
            ["DeviceConnections"] = "TimestampUtc",
            ["NetworkSnapshots"] = "TimestampUtc",
            ["RouterHealthSnapshots"] = "TimestampUtc",
            ["DnsSnapshots"] = "TimestampUtc"
        };

        foreach (string tableName in tableNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string quotedTable = '"' + tableName.Replace("\"", "\"\"") + '"';
            long rowCount;
            await using (var countCommand = connection.CreateCommand())
            {
                countCommand.CommandText = $"SELECT COUNT(*) FROM {quotedTable};";
                rowCount = Convert.ToInt64(await countCommand
                    .ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
            }

            string? oldest = null;
            string? newest = null;
            if (timestampColumns.TryGetValue(tableName, out string? timestampColumn))
            {
                await using var rangeCommand = connection.CreateCommand();
                rangeCommand.CommandText =
                    $"SELECT MIN(\"{timestampColumn}\"), MAX(\"{timestampColumn}\") FROM {quotedTable};";
                await using var reader = await rangeCommand.ExecuteReaderAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    oldest = reader.IsDBNull(0) ? null : reader.GetString(0);
                    newest = reader.IsDBNull(1) ? null : reader.GetString(1);
                }
            }
            tables.Add(new DatabaseTableHealth(tableName, rowCount, oldest, newest));
        }

        string integrity;
        await using (var integrityCommand = connection.CreateCommand())
        {
            integrityCommand.CommandText = "PRAGMA integrity_check;";
            integrity = Convert.ToString(await integrityCommand
                .ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) ?? "Unavailable";
        }

        int schemaVersion = 0;
        if (tables.Any(table => table.Name == "SchemaVersion"))
        {
            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText =
                "SELECT Version FROM SchemaVersion WHERE Id = 1;";
            object? value = await versionCommand.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);
            schemaVersion = value is null or DBNull ? 0 : Convert.ToInt32(value);
        }
        long size = File.Exists(_dataStore.DatabasePath)
            ? new FileInfo(_dataStore.DatabasePath).Length
            : 0;
        return new DatabaseHealthReport
        {
            DatabasePath = _dataStore.DatabasePath,
            FileSizeBytes = size,
            SchemaVersion = schemaVersion,
            IntegrityCheck = integrity,
            Tables = tables
        };
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

    public async Task<IReadOnlyList<DeviceConnectionEvent>> GetRecentDeviceEventsAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0)
            return Array.Empty<DeviceConnectionEvent>();

        await using var connection = await _dataStore
            .OpenReadOnlyConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, MacAddress, TimestampUtc, EventType, IpAddress,
                   NetworkName, Hostname, FriendlyName
            FROM DeviceConnections
            ORDER BY TimestampUtc DESC, Id DESC
            LIMIT $maximumCount;
            """;
        command.Parameters.AddWithValue("$maximumCount", maximumCount);
        return await ReadEventsAsync(command, cancellationToken);
    }

    public async Task AddOrUpdateWanMinuteAsync(
        WanMinuteSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await using var connection =
            await _dataStore.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO NetworkSnapshots
                (TimestampUtc, IsWanConnected, DownloadMbps, UploadMbps,
                 AverageDownloadMbps, AverageUploadMbps,
                 PeakDownloadMbps, PeakUploadMbps,
                 ReceivedBytesTotal, TransmittedBytesTotal, SampleCount)
            VALUES
                ($timestampUtc, 1, $averageDownload, $averageUpload,
                 $averageDownload, $averageUpload,
                 $peakDownload, $peakUpload,
                 $receivedTotal, $transmittedTotal, $sampleCount)
            ON CONFLICT(TimestampUtc) DO UPDATE SET
                IsWanConnected = 1,
                DownloadMbps = excluded.DownloadMbps,
                UploadMbps = excluded.UploadMbps,
                AverageDownloadMbps = excluded.AverageDownloadMbps,
                AverageUploadMbps = excluded.AverageUploadMbps,
                PeakDownloadMbps = excluded.PeakDownloadMbps,
                PeakUploadMbps = excluded.PeakUploadMbps,
                ReceivedBytesTotal = excluded.ReceivedBytesTotal,
                TransmittedBytesTotal = excluded.TransmittedBytesTotal,
                SampleCount = excluded.SampleCount;
            """;
        command.Parameters.AddWithValue(
            "$timestampUtc",
            snapshot.TimestampUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$averageDownload", snapshot.AverageDownloadMbps);
        command.Parameters.AddWithValue("$averageUpload", snapshot.AverageUploadMbps);
        command.Parameters.AddWithValue("$peakDownload", snapshot.PeakDownloadMbps);
        command.Parameters.AddWithValue("$peakUpload", snapshot.PeakUploadMbps);
        command.Parameters.AddWithValue("$receivedTotal", snapshot.ReceivedBytesTotal);
        command.Parameters.AddWithValue("$transmittedTotal", snapshot.TransmittedBytesTotal);
        command.Parameters.AddWithValue("$sampleCount", snapshot.SampleCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WanMinuteSnapshot>> GetWanHistoryAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        if (toUtc < fromUtc)
            throw new ArgumentOutOfRangeException(nameof(toUtc));

        await using var connection =
            await _dataStore.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, TimestampUtc,
                   COALESCE(AverageDownloadMbps, DownloadMbps, 0),
                   COALESCE(AverageUploadMbps, UploadMbps, 0),
                   COALESCE(PeakDownloadMbps, DownloadMbps, 0),
                   COALESCE(PeakUploadMbps, UploadMbps, 0),
                   COALESCE(ReceivedBytesTotal, 0),
                   COALESCE(TransmittedBytesTotal, 0),
                   COALESCE(SampleCount, 1)
            FROM NetworkSnapshots
            WHERE TimestampUtc >= $fromUtc AND TimestampUtc <= $toUtc
            ORDER BY TimestampUtc ASC, Id ASC;
            """;
        command.Parameters.AddWithValue("$fromUtc", fromUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$toUtc", toUtc.ToUniversalTime().ToString("O"));

        var snapshots = new List<WanMinuteSnapshot>();
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            snapshots.Add(new WanMinuteSnapshot
            {
                Id = reader.GetInt64(0),
                TimestampUtc = DateTimeOffset.Parse(reader.GetString(1)),
                AverageDownloadMbps = reader.GetDouble(2),
                AverageUploadMbps = reader.GetDouble(3),
                PeakDownloadMbps = reader.GetDouble(4),
                PeakUploadMbps = reader.GetDouble(5),
                ReceivedBytesTotal = reader.GetInt64(6),
                TransmittedBytesTotal = reader.GetInt64(7),
                SampleCount = reader.GetInt32(8)
            });
        }

        return snapshots;
    }

    public async Task<int> DeleteWanHistoryBeforeAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await _dataStore.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM NetworkSnapshots WHERE TimestampUtc < $cutoffUtc;";
        command.Parameters.AddWithValue(
            "$cutoffUtc",
            cutoffUtc.ToUniversalTime().ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddOrUpdateRouterHealthMinuteAsync(
        RouterHealthMinuteSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await using var connection =
            await _dataStore.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RouterHealthSnapshots
                (TimestampUtc, CpuUsagePercent, MemoryUsagePercent,
                 StorageUsagePercent, AverageCpuUsagePercent,
                 PeakCpuUsagePercent, AverageMemoryUsagePercent,
                 PeakMemoryUsagePercent, MemoryUsedBytes,
                 MemoryTotalBytes, TemperatureCelsius, SampleCount)
            VALUES
                ($timestampUtc, $averageCpu, $averageMemory,
                 $storage, $averageCpu, $peakCpu, $averageMemory,
                 $peakMemory, $memoryUsed, $memoryTotal,
                 $temperature, $sampleCount)
            ON CONFLICT(TimestampUtc) DO UPDATE SET
                CpuUsagePercent = excluded.CpuUsagePercent,
                MemoryUsagePercent = excluded.MemoryUsagePercent,
                StorageUsagePercent = excluded.StorageUsagePercent,
                AverageCpuUsagePercent = excluded.AverageCpuUsagePercent,
                PeakCpuUsagePercent = excluded.PeakCpuUsagePercent,
                AverageMemoryUsagePercent = excluded.AverageMemoryUsagePercent,
                PeakMemoryUsagePercent = excluded.PeakMemoryUsagePercent,
                MemoryUsedBytes = excluded.MemoryUsedBytes,
                MemoryTotalBytes = excluded.MemoryTotalBytes,
                TemperatureCelsius = excluded.TemperatureCelsius,
                SampleCount = excluded.SampleCount;
            """;
        command.Parameters.AddWithValue(
            "$timestampUtc",
            snapshot.TimestampUtc.ToUniversalTime().ToString("O"));
        AddNullable(command, "$averageCpu", snapshot.AverageCpuUsagePercent);
        AddNullable(command, "$peakCpu", snapshot.PeakCpuUsagePercent);
        AddNullable(command, "$averageMemory", snapshot.AverageMemoryUsagePercent);
        AddNullable(command, "$peakMemory", snapshot.PeakMemoryUsagePercent);
        AddNullable(command, "$memoryUsed", snapshot.MemoryUsedBytes);
        AddNullable(command, "$memoryTotal", snapshot.MemoryTotalBytes);
        AddNullable(command, "$temperature", snapshot.TemperatureCelsius);
        AddNullable(command, "$storage", snapshot.StorageUsagePercent);
        command.Parameters.AddWithValue("$sampleCount", snapshot.SampleCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RouterHealthMinuteSnapshot>>
        GetRouterHealthHistoryAsync(
            DateTimeOffset fromUtc,
            DateTimeOffset toUtc,
            CancellationToken cancellationToken = default)
    {
        if (toUtc < fromUtc)
            throw new ArgumentOutOfRangeException(nameof(toUtc));

        await using var connection =
            await _dataStore.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, TimestampUtc,
                   COALESCE(AverageCpuUsagePercent, CpuUsagePercent),
                   COALESCE(PeakCpuUsagePercent, CpuUsagePercent),
                   COALESCE(AverageMemoryUsagePercent, MemoryUsagePercent),
                   COALESCE(PeakMemoryUsagePercent, MemoryUsagePercent),
                   MemoryUsedBytes, MemoryTotalBytes,
                   TemperatureCelsius, StorageUsagePercent,
                   COALESCE(SampleCount, 1)
            FROM RouterHealthSnapshots
            WHERE TimestampUtc >= $fromUtc AND TimestampUtc <= $toUtc
            ORDER BY TimestampUtc ASC, Id ASC;
            """;
        command.Parameters.AddWithValue("$fromUtc", fromUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$toUtc", toUtc.ToUniversalTime().ToString("O"));

        var snapshots = new List<RouterHealthMinuteSnapshot>();
        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            snapshots.Add(new RouterHealthMinuteSnapshot
            {
                Id = reader.GetInt64(0),
                TimestampUtc = DateTimeOffset.Parse(reader.GetString(1)),
                AverageCpuUsagePercent = GetNullableDouble(reader, 2),
                PeakCpuUsagePercent = GetNullableDouble(reader, 3),
                AverageMemoryUsagePercent = GetNullableDouble(reader, 4),
                PeakMemoryUsagePercent = GetNullableDouble(reader, 5),
                MemoryUsedBytes = GetNullableInt64(reader, 6),
                MemoryTotalBytes = GetNullableInt64(reader, 7),
                TemperatureCelsius = GetNullableDouble(reader, 8),
                StorageUsagePercent = GetNullableDouble(reader, 9),
                SampleCount = reader.GetInt32(10)
            });
        }

        return snapshots;
    }

    public async Task<int> DeleteRouterHealthBeforeAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await _dataStore.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM RouterHealthSnapshots WHERE TimestampUtc < $cutoffUtc;";
        command.Parameters.AddWithValue(
            "$cutoffUtc",
            cutoffUtc.ToUniversalTime().ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WanHistoryAggregate> GetWanAggregateAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await _dataStore.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            WITH filtered AS (
                SELECT Id, TimestampUtc,
                       COALESCE(AverageDownloadMbps, DownloadMbps) AS AvgDownload,
                       COALESCE(AverageUploadMbps, UploadMbps) AS AvgUpload,
                       COALESCE(PeakDownloadMbps, DownloadMbps) AS PeakDownload,
                       COALESCE(PeakUploadMbps, UploadMbps) AS PeakUpload,
                       COALESCE(SampleCount, 1) AS Samples,
                       ReceivedBytesTotal, TransmittedBytesTotal
                FROM NetworkSnapshots
                WHERE TimestampUtc >= $fromUtc AND TimestampUtc <= $toUtc
            ), ordered AS (
                SELECT *,
                       LAG(ReceivedBytesTotal) OVER
                           (ORDER BY TimestampUtc, Id) AS PreviousReceived,
                       LAG(TransmittedBytesTotal) OVER
                           (ORDER BY TimestampUtc, Id) AS PreviousTransmitted
                FROM filtered
            )
            SELECT COUNT(*),
                   SUM(AvgDownload * Samples) /
                       NULLIF(SUM(CASE WHEN AvgDownload IS NOT NULL THEN Samples ELSE 0 END), 0),
                   MAX(PeakDownload),
                   SUM(AvgUpload * Samples) /
                       NULLIF(SUM(CASE WHEN AvgUpload IS NOT NULL THEN Samples ELSE 0 END), 0),
                   MAX(PeakUpload),
                   (SELECT ReceivedBytesTotal FROM filtered
                    WHERE ReceivedBytesTotal IS NOT NULL
                    ORDER BY TimestampUtc, Id LIMIT 1),
                   (SELECT ReceivedBytesTotal FROM filtered
                    WHERE ReceivedBytesTotal IS NOT NULL
                    ORDER BY TimestampUtc DESC, Id DESC LIMIT 1),
                   COALESCE(SUM(CASE WHEN PreviousReceived IS NOT NULL AND
                       ReceivedBytesTotal < PreviousReceived THEN 1 ELSE 0 END), 0),
                   (SELECT TransmittedBytesTotal FROM filtered
                    WHERE TransmittedBytesTotal IS NOT NULL
                    ORDER BY TimestampUtc, Id LIMIT 1),
                   (SELECT TransmittedBytesTotal FROM filtered
                    WHERE TransmittedBytesTotal IS NOT NULL
                    ORDER BY TimestampUtc DESC, Id DESC LIMIT 1),
                   COALESCE(SUM(CASE WHEN PreviousTransmitted IS NOT NULL AND
                       TransmittedBytesTotal < PreviousTransmitted THEN 1 ELSE 0 END), 0)
            FROM ordered;
            """;
        AddPeriodParameters(command, fromUtc, toUtc);

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new WanHistoryAggregate();

        int count = Convert.ToInt32(reader.GetInt64(0));
        long? firstReceived = GetNullableInt64(reader, 5);
        long? lastReceived = GetNullableInt64(reader, 6);
        long receivedResets = reader.GetInt64(7);
        long? firstTransmitted = GetNullableInt64(reader, 8);
        long? lastTransmitted = GetNullableInt64(reader, 9);
        long transmittedResets = reader.GetInt64(10);

        return new WanHistoryAggregate
        {
            DataPointCount = count,
            AverageDownloadMbps = GetNullableDouble(reader, 1),
            PeakDownloadMbps = GetNullableDouble(reader, 2),
            AverageUploadMbps = GetNullableDouble(reader, 3),
            PeakUploadMbps = GetNullableDouble(reader, 4),
            TotalDownloadBytes = ReliableCounterDelta(
                count,
                firstReceived,
                lastReceived,
                receivedResets),
            TotalUploadBytes = ReliableCounterDelta(
                count,
                firstTransmitted,
                lastTransmitted,
                transmittedResets)
        };
    }

    public async Task<RouterHealthAggregate> GetRouterHealthAggregateAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await _dataStore.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*),
                   SUM(COALESCE(AverageCpuUsagePercent, CpuUsagePercent) *
                       COALESCE(SampleCount, 1)) /
                       NULLIF(SUM(CASE WHEN COALESCE(AverageCpuUsagePercent,
                           CpuUsagePercent) IS NOT NULL THEN
                           COALESCE(SampleCount, 1) ELSE 0 END), 0),
                   MAX(COALESCE(PeakCpuUsagePercent, CpuUsagePercent)),
                   SUM(COALESCE(AverageMemoryUsagePercent, MemoryUsagePercent) *
                       COALESCE(SampleCount, 1)) /
                       NULLIF(SUM(CASE WHEN COALESCE(AverageMemoryUsagePercent,
                           MemoryUsagePercent) IS NOT NULL THEN
                           COALESCE(SampleCount, 1) ELSE 0 END), 0),
                   MAX(COALESCE(PeakMemoryUsagePercent, MemoryUsagePercent))
            FROM RouterHealthSnapshots
            WHERE TimestampUtc >= $fromUtc AND TimestampUtc <= $toUtc;
            """;
        AddPeriodParameters(command, fromUtc, toUtc);

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return new RouterHealthAggregate();

        return new RouterHealthAggregate
        {
            DataPointCount = Convert.ToInt32(reader.GetInt64(0)),
            AverageCpuPercent = GetNullableDouble(reader, 1),
            PeakCpuPercent = GetNullableDouble(reader, 2),
            AverageMemoryPercent = GetNullableDouble(reader, 3),
            PeakMemoryPercent = GetNullableDouble(reader, 4)
        };
    }

    public async Task<DeviceConnectionAggregate> GetDeviceConnectionAggregateAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            await _dataStore.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand countCommand = connection.CreateCommand();
        countCommand.CommandText = """
            SELECT COUNT(*) FROM DeviceConnections
            WHERE TimestampUtc >= $fromUtc AND TimestampUtc <= $toUtc;
            """;
        AddPeriodParameters(countCommand, fromUtc, toUtc);
        int eventCount = Convert.ToInt32(
            (long)(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0L));

        await using SqliteCommand networkCommand = connection.CreateCommand();
        networkCommand.CommandText = """
            SELECT NetworkName, COUNT(*) AS ActivityCount
            FROM DeviceConnections
            WHERE TimestampUtc >= $fromUtc AND TimestampUtc <= $toUtc
              AND EventType IN (0, 4)
              AND NetworkName IS NOT NULL AND TRIM(NetworkName) <> ''
            GROUP BY NetworkName
            ORDER BY ActivityCount DESC, NetworkName ASC
            LIMIT 1;
            """;
        AddPeriodParameters(networkCommand, fromUtc, toUtc);
        object? network = await networkCommand.ExecuteScalarAsync(cancellationToken);

        return new DeviceConnectionAggregate
        {
            EventCount = eventCount,
            MostActiveNetworkName = network is null || network is DBNull
                ? null
                : Convert.ToString(network)
        };
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

    private static void AddNullable<T>(
        SqliteCommand command,
        string name,
        T? value)
        where T : struct =>
        command.Parameters.AddWithValue(name, value.HasValue ? value.Value : DBNull.Value);

    private static void AddPeriodParameters(
        SqliteCommand command,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        command.Parameters.AddWithValue(
            "$fromUtc",
            fromUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue(
            "$toUtc",
            toUtc.ToUniversalTime().ToString("O"));
    }

    private static long? ReliableCounterDelta(
        int dataPointCount,
        long? first,
        long? last,
        long resetCount) =>
        dataPointCount >= 2 &&
        resetCount == 0 &&
        first.HasValue &&
        last.HasValue &&
        last.Value >= first.Value
            ? last.Value - first.Value
            : null;

    private static double? GetNullableDouble(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static long? GetNullableInt64(
        SqliteDataReader reader,
        int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
}
