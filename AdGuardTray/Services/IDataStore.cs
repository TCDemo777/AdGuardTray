using Microsoft.Data.Sqlite;

namespace AdGuardTray.Services;

public interface IDataStore
{
    string DatabasePath { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default);

    Task<SqliteConnection> OpenReadOnlyConnectionAsync(
        CancellationToken cancellationToken = default);
}
