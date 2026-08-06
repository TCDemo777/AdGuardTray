using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using RouterPilot.Models;

namespace RouterPilot.Services;

public sealed class DiagnosticsHistoryService
{
    private const int MaximumEntries = 50;
    private readonly Dispatcher _dispatcher;
    private readonly ObservableCollection<DiagnosticHistoryEntry> _entries = new();

    public DiagnosticsHistoryService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Entries = new ReadOnlyObservableCollection<DiagnosticHistoryEntry>(_entries);
    }

    public ReadOnlyObservableCollection<DiagnosticHistoryEntry> Entries { get; }

    public event EventHandler? HistoryChanged;

    public Task AddAsync(
        DiagnosticExecutionOutcome outcome,
        string message,
        DiagnosticExecutionSource source,
        string? outputPath,
        CancellationToken cancellationToken = default)
    {
        return _dispatcher.InvokeAsync(() =>
        {
            _entries.Insert(0, new DiagnosticHistoryEntry
            {
                Outcome = outcome,
                Message = message,
                Source = source,
                OutputPath = outputPath
            });

            while (_entries.Count > MaximumEntries)
            {
                _entries.RemoveAt(_entries.Count - 1);
            }

            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.DataBind, cancellationToken).Task;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return _dispatcher.InvokeAsync(() =>
        {
            _entries.Clear();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.DataBind, cancellationToken).Task;
    }

    public string GetLogText()
    {
        return string.Join(
            Environment.NewLine,
            Entries.Reverse().Select(entry => entry.DisplayText));
    }
}
