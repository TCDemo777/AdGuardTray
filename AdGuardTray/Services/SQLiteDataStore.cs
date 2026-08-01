using System.IO;
using Microsoft.Data.Sqlite;

namespace AdGuardTray.Services;

public sealed class SQLiteDataStore : IDataStore, IAsyncDisposable
{
    private readonly DatabaseInitializer _initializer;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public SQLiteDataStore(DatabaseInitializer initializer)
        : this(initializer, null)
    {
    }

    internal SQLiteDataStore(
        DatabaseInitializer initializer,
        string? dataFolder)
    {
        _initializer = initializer;

        string folder = dataFolder ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "AdGuardTray");

        DatabasePath = Path.Combine(folder, "RouterPilot.db");
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            string? directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await using SqliteConnection connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await _initializer.InitializeAsync(connection, cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await InitializeAsync(cancellationToken);

        SqliteConnection connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task<SqliteConnection> OpenReadOnlyConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await InitializeAsync(cancellationToken);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _initializationGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true
        };

        return new SqliteConnection(builder.ToString());
    }
}
