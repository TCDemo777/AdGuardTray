using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using RouterPilot.Models;

namespace RouterPilot.Services;

public sealed class MaintenanceHistoryService
{
    private const int MaximumEntries = 50;
    private readonly Dispatcher _dispatcher;
    private readonly string _path;
    private readonly ObservableCollection<MaintenanceHistoryEntry> _entries = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public MaintenanceHistoryService(Dispatcher dispatcher, ApplicationDataPathProvider paths)
    {
        _dispatcher = dispatcher;
        _path = Path.Combine(paths.CurrentPath, "maintenance-history.json");
        Entries = new ReadOnlyObservableCollection<MaintenanceHistoryEntry>(_entries);
    }

    public ReadOnlyObservableCollection<MaintenanceHistoryEntry> Entries { get; }
    public event EventHandler? Changed;

    public async Task InitializeAsync()
    {
        List<MaintenanceHistoryEntry> loaded = [];
        try
        {
            if (File.Exists(_path))
            {
                await using FileStream stream = File.OpenRead(_path);
                loaded = await JsonSerializer.DeserializeAsync<List<MaintenanceHistoryEntry>>(stream)
                    ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt maintenance history must never prevent application startup.
        }

        await _dispatcher.InvokeAsync(() =>
        {
            _entries.Clear();
            foreach (MaintenanceHistoryEntry entry in loaded
                         .OrderByDescending(entry => entry.Timestamp)
                         .Take(MaximumEntries))
            {
                _entries.Add(entry);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    public async Task AddAsync(MaintenanceHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _dispatcher.InvokeAsync(() =>
        {
            _entries.Insert(0, entry);
            while (_entries.Count > MaximumEntries)
                _entries.RemoveAt(_entries.Count - 1);
            Changed?.Invoke(this, EventArgs.Empty);
        });

        await FlushAsync();
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            List<MaintenanceHistoryEntry> snapshot = await _dispatcher.InvokeAsync(
                () => _entries.ToList(), DispatcherPriority.Send, cancellationToken);

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporaryPath = _path + ".tmp";
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
