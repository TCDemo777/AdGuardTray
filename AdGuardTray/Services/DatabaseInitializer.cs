using Microsoft.Data.Sqlite;

namespace AdGuardTray.Services;

public sealed class DatabaseInitializer
{
    public const int CurrentSchemaVersion = 4;

    public async Task InitializeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken = default)
    {
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS SchemaVersion (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                Version INTEGER NOT NULL,
                AppliedAtUtc TEXT NOT NULL
            );
            """, cancellationToken);

        int version = await GetSchemaVersionAsync(
            connection,
            transaction,
            cancellationToken);

        if (version < 1)
        {
            await CreateVersionOneSchemaAsync(
                connection,
                transaction,
                cancellationToken);

            version = 1;
            await SetSchemaVersionAsync(
                connection,
                transaction,
                version,
                cancellationToken);
        }

        if (version < 2)
        {
            await UpgradeToVersionTwoAsync(
                connection,
                transaction,
                cancellationToken);
            version = 2;
            await SetSchemaVersionAsync(
                connection,
                transaction,
                version,
                cancellationToken);
        }

        if (version < 3)
        {
            await UpgradeToVersionThreeAsync(
                connection,
                transaction,
                cancellationToken);
            version = 3;
            await SetSchemaVersionAsync(
                connection,
                transaction,
                version,
                cancellationToken);
        }

        if (version < 4)
        {
            await UpgradeToVersionFourAsync(
                connection,
                transaction,
                cancellationToken);
            version = 4;
            await SetSchemaVersionAsync(
                connection,
                transaction,
                version,
                cancellationToken);
        }

        if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {version} is newer than supported version {CurrentSchemaVersion}.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task SetSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SchemaVersion (Id, Version, AppliedAtUtc)
            VALUES (1, $version, $appliedAtUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Version = excluded.Version,
                AppliedAtUtc = excluded.AppliedAtUtc;
            """;
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue(
            "$appliedAtUtc",
            DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> GetSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Version FROM SchemaVersion WHERE Id = 1;";
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null || result is DBNull ? 0 : Convert.ToInt32(result);
    }

    private static async Task CreateVersionOneSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string schema = """
            CREATE TABLE IF NOT EXISTS DeviceHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MacAddress TEXT NOT NULL UNIQUE,
                FriendlyName TEXT NULL,
                Hostname TEXT NULL,
                Manufacturer TEXT NULL,
                DeviceType TEXT NULL,
                FirstSeenUtc TEXT NOT NULL,
                LastSeenUtc TEXT NOT NULL,
                LastIpAddress TEXT NULL,
                LastNetworkName TEXT NULL,
                LastSsid TEXT NULL,
                IsCurrentlyOnline INTEGER NOT NULL DEFAULT 0,
                TimesSeenOnline INTEGER NOT NULL DEFAULT 0,
                TimesConnected INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS DeviceConnections (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceHistoryId INTEGER NOT NULL,
                ConnectedAtUtc TEXT NOT NULL,
                DisconnectedAtUtc TEXT NULL,
                IpAddress TEXT NULL,
                NetworkName TEXT NULL,
                Ssid TEXT NULL,
                FOREIGN KEY (DeviceHistoryId) REFERENCES DeviceHistory(Id)
                    ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS NetworkSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimestampUtc TEXT NOT NULL,
                IsWanConnected INTEGER NOT NULL,
                PublicIpAddress TEXT NULL,
                DownloadMbps REAL NULL,
                UploadMbps REAL NULL,
                ConnectedClientCount INTEGER NULL
            );

            CREATE TABLE IF NOT EXISTS RouterHealthSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimestampUtc TEXT NOT NULL,
                CpuUsagePercent REAL NULL,
                MemoryUsagePercent REAL NULL,
                StorageUsagePercent REAL NULL,
                UptimeSeconds INTEGER NULL,
                LatencyMilliseconds REAL NULL
            );

            CREATE TABLE IF NOT EXISTS DnsSnapshots (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimestampUtc TEXT NOT NULL,
                TotalQueries INTEGER NULL,
                BlockedQueries INTEGER NULL,
                ProcessingTimeMilliseconds REAL NULL,
                ProtectionEnabled INTEGER NULL
            );

            CREATE INDEX IF NOT EXISTS IX_DeviceConnections_DeviceHistoryId_ConnectedAtUtc
                ON DeviceConnections (DeviceHistoryId, ConnectedAtUtc);
            CREATE INDEX IF NOT EXISTS IX_NetworkSnapshots_TimestampUtc
                ON NetworkSnapshots (TimestampUtc);
            CREATE INDEX IF NOT EXISTS IX_RouterHealthSnapshots_TimestampUtc
                ON RouterHealthSnapshots (TimestampUtc);
            CREATE INDEX IF NOT EXISTS IX_DnsSnapshots_TimestampUtc
                ON DnsSnapshots (TimestampUtc);
            """;

        await ExecuteAsync(connection, transaction, schema, cancellationToken);
    }

    private static async Task UpgradeToVersionTwoAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string migration = """
            DROP INDEX IF EXISTS IX_DeviceConnections_DeviceHistoryId_ConnectedAtUtc;
            ALTER TABLE DeviceConnections RENAME TO DeviceConnections_V1;

            CREATE TABLE DeviceConnections (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MacAddress TEXT NOT NULL,
                TimestampUtc TEXT NOT NULL,
                EventType INTEGER NOT NULL,
                IpAddress TEXT NULL,
                NetworkName TEXT NULL,
                Hostname TEXT NULL,
                FriendlyName TEXT NULL
            );

            CREATE INDEX IX_DeviceConnections_MacAddress_TimestampUtc
                ON DeviceConnections (MacAddress, TimestampUtc DESC);

            INSERT INTO DeviceConnections
                (MacAddress, TimestampUtc, EventType, IpAddress,
                 NetworkName, Hostname, FriendlyName)
            SELECT history.MacAddress, legacy.ConnectedAtUtc, 0,
                   legacy.IpAddress, legacy.NetworkName,
                   history.Hostname, history.FriendlyName
            FROM DeviceConnections_V1 AS legacy
            INNER JOIN DeviceHistory AS history
                ON history.Id = legacy.DeviceHistoryId;

            INSERT INTO DeviceConnections
                (MacAddress, TimestampUtc, EventType, IpAddress,
                 NetworkName, Hostname, FriendlyName)
            SELECT history.MacAddress, legacy.DisconnectedAtUtc, 1,
                   legacy.IpAddress, legacy.NetworkName,
                   history.Hostname, history.FriendlyName
            FROM DeviceConnections_V1 AS legacy
            INNER JOIN DeviceHistory AS history
                ON history.Id = legacy.DeviceHistoryId
            WHERE legacy.DisconnectedAtUtc IS NOT NULL;

            DROP TABLE DeviceConnections_V1;
            """;

        await ExecuteAsync(connection, transaction, migration, cancellationToken);
    }

    private static async Task UpgradeToVersionThreeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string migration = """
            ALTER TABLE NetworkSnapshots ADD COLUMN AverageDownloadMbps REAL NULL;
            ALTER TABLE NetworkSnapshots ADD COLUMN AverageUploadMbps REAL NULL;
            ALTER TABLE NetworkSnapshots ADD COLUMN PeakDownloadMbps REAL NULL;
            ALTER TABLE NetworkSnapshots ADD COLUMN PeakUploadMbps REAL NULL;
            ALTER TABLE NetworkSnapshots ADD COLUMN ReceivedBytesTotal INTEGER NULL;
            ALTER TABLE NetworkSnapshots ADD COLUMN TransmittedBytesTotal INTEGER NULL;
            ALTER TABLE NetworkSnapshots ADD COLUMN SampleCount INTEGER NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS UX_NetworkSnapshots_TimestampUtc
                ON NetworkSnapshots (TimestampUtc);
            """;

        await ExecuteAsync(connection, transaction, migration, cancellationToken);
    }

    private static async Task UpgradeToVersionFourAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string migration = """
            ALTER TABLE RouterHealthSnapshots ADD COLUMN AverageCpuUsagePercent REAL NULL;
            ALTER TABLE RouterHealthSnapshots ADD COLUMN PeakCpuUsagePercent REAL NULL;
            ALTER TABLE RouterHealthSnapshots ADD COLUMN AverageMemoryUsagePercent REAL NULL;
            ALTER TABLE RouterHealthSnapshots ADD COLUMN PeakMemoryUsagePercent REAL NULL;
            ALTER TABLE RouterHealthSnapshots ADD COLUMN MemoryUsedBytes INTEGER NULL;
            ALTER TABLE RouterHealthSnapshots ADD COLUMN MemoryTotalBytes INTEGER NULL;
            ALTER TABLE RouterHealthSnapshots ADD COLUMN TemperatureCelsius REAL NULL;
            ALTER TABLE RouterHealthSnapshots ADD COLUMN SampleCount INTEGER NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS UX_RouterHealthSnapshots_TimestampUtc
                ON RouterHealthSnapshots (TimestampUtc);
            """;

        await ExecuteAsync(connection, transaction, migration, cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
