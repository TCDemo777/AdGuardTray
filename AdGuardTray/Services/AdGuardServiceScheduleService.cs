using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using AdGuardTray.Models;

namespace AdGuardTray.Services;

public sealed class AdGuardServiceScheduleService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ObservableCollection<AdGuardServiceSchedule> _items = [];
    private readonly Dispatcher _dispatcher;
    private readonly BlockedServiceMutationService _mutations;
    private readonly NotificationService _notifications;
    private readonly IClock _clock;
    private readonly AdGuardServiceScheduleCalculator _calculator;
    private readonly SemaphoreSlim _evaluationGate = new(1, 1);
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly string _path;
    private readonly object _disposeSync = new();
    private bool _disposing;
    private bool _disposed;
    private Task? _disposeTask;

    public AdGuardServiceScheduleService(
        Dispatcher dispatcher, BlockedServiceMutationService mutations,
        NotificationService notifications, IClock clock)
    {
        _dispatcher = dispatcher;
        _mutations = mutations;
        _notifications = notifications;
        _clock = clock;
        _calculator = new(clock);
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdGuardTray");
        _path = Path.Combine(folder, "adguard-service-schedules.json");
        Schedules = new ReadOnlyObservableCollection<AdGuardServiceSchedule>(_items);
    }

    public ReadOnlyObservableCollection<AdGuardServiceSchedule> Schedules { get; }
    public TimeSpan MissedOccurrenceGracePeriod { get; set; } = TimeSpan.FromMinutes(30);
    public event EventHandler<BlockedServiceMutationResult>? BlockedServicesChanged;

    public async Task InitializeAsync()
    {
        List<AdGuardServiceSchedule> loaded = [];
        try
        {
            if (File.Exists(_path))
                loaded = JsonSerializer.Deserialize<List<AdGuardServiceSchedule>>(await File.ReadAllTextAsync(_path), JsonOptions) ?? [];
        }
        catch (Exception ex) { Debug.WriteLine($"Unable to load AdGuard service schedules: {ex.Message}"); }

        await _dispatcher.InvokeAsync(() =>
        {
            foreach (AdGuardServiceSchedule item in loaded)
            {
                Normalize(item);
                item.NextExecutionLocal = item.IsEnabled ? _calculator.Next(item, _clock.UtcNow) : null;
                _items.Add(item);
            }
        });
    }

    public async Task SaveScheduleAsync(AdGuardServiceSchedule schedule)
    {
        Normalize(schedule);
        schedule.NextExecutionLocal = schedule.IsEnabled ? _calculator.Next(schedule, _clock.UtcNow) : null;
        await _dispatcher.InvokeAsync(() =>
        {
            int index = _items.ToList().FindIndex(x => x.Id == schedule.Id);
            if (index >= 0) _items[index] = schedule; else _items.Insert(0, schedule);
        });
        await SaveAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        await _dispatcher.InvokeAsync(() => { AdGuardServiceSchedule? item = _items.FirstOrDefault(x => x.Id == id); if (item is not null) _items.Remove(item); });
        await SaveAsync();
    }

    public async Task DuplicateAsync(AdGuardServiceSchedule source)
    {
        AdGuardServiceSchedule copy = Clone(source);
        copy.Id = Guid.NewGuid(); copy.GroupId = null; copy.Name += " (copy)";
        copy.CreatedUtc = _clock.UtcNow; copy.LastExecutedUtc = null; copy.LastAttemptedOccurrenceUtc = null;
        await SaveScheduleAsync(copy);
    }

    public async Task CreateAllowedWindowAsync(string serviceId, string serviceName, TimeOnly allowAt, TimeOnly blockAt, ScheduleDays days)
    {
        Guid group = Guid.NewGuid();
        await SaveScheduleAsync(new() { GroupId = group, Name = $"Allow {serviceName}", ServiceIds = [serviceId], Action = AdGuardServiceScheduleAction.Allow, LocalTime = allowAt, Recurrence = AdGuardServiceScheduleRecurrence.SelectedDays, SelectedDays = days, CreatedUtc = _clock.UtcNow });
        // A closing time after midnight belongs to the following selected calendar day.
        ScheduleDays blockDays = blockAt <= allowAt ? ShiftDays(days) : days;
        await SaveScheduleAsync(new() { GroupId = group, Name = $"Block {serviceName}", ServiceIds = [serviceId], Action = AdGuardServiceScheduleAction.Block, LocalTime = blockAt, Recurrence = AdGuardServiceScheduleRecurrence.SelectedDays, SelectedDays = blockDays, CreatedUtc = _clock.UtcNow });
    }

    public async Task EvaluateDueAsync(CancellationToken token)
    {
        if (!await _evaluationGate.WaitAsync(0, token)) return;
        try
        {
            DateTimeOffset now = _clock.UtcNow;
            AdGuardServiceSchedule[] snapshot = await _dispatcher.InvokeAsync(() => _items.Where(x => x.IsEnabled).ToArray());
            foreach (AdGuardServiceSchedule schedule in snapshot)
            {
                token.ThrowIfCancellationRequested();
                DateTimeOffset? due = _calculator.DueOccurrence(schedule, now, MissedOccurrenceGracePeriod);
                if (due is null) { schedule.NextExecutionLocal = _calculator.Next(schedule, now); continue; }
                DateTimeOffset occurrenceUtc = due.Value.ToUniversalTime();
                if (schedule.LastExecutedUtc == occurrenceUtc || schedule.LastAttemptedOccurrenceUtc == occurrenceUtc) continue;
                await ExecuteAsync(schedule, occurrenceUtc, false, token);
            }
        }
        finally { _evaluationGate.Release(); }
    }

    public async Task RunNowAsync(AdGuardServiceSchedule schedule, CancellationToken token = default)
    {
        if (_disposing || _disposed) throw new OperationCanceledException("Schedule service is shutting down.");
        if (!await _evaluationGate.WaitAsync(0, token))
            throw new InvalidOperationException("Another scheduled service change is already running.");
        try { await ExecuteAsync(schedule, _clock.UtcNow, true, token); }
        finally { _evaluationGate.Release(); }
    }

    private async Task ExecuteAsync(AdGuardServiceSchedule schedule, DateTimeOffset occurrenceUtc, bool runNow, CancellationToken token)
    {
        if (!runNow)
        {
            schedule.LastAttemptedOccurrenceUtc = occurrenceUtc;
            await SaveAsync();
        }
        try
        {
            BlockedServiceMutationResult result = await _mutations.ApplyAsync(schedule.ServiceIds, schedule.Action, token);
            if (!runNow)
            {
                schedule.LastExecutedUtc = occurrenceUtc;
                if (schedule.Recurrence == AdGuardServiceScheduleRecurrence.Once) schedule.IsEnabled = false;
                schedule.NextExecutionLocal = schedule.IsEnabled ? _calculator.Next(schedule, occurrenceUtc) : null;
            }
            schedule.LastError = null; schedule.LastErrorUtc = null;
            BlockedServicesChanged?.Invoke(this, result);
            await RefreshItemAsync(schedule);
            await SaveAsync();
            string names = string.IsNullOrWhiteSpace(schedule.ServiceDisplay) ? schedule.Name : schedule.ServiceDisplay;
            await _notifications.AddAsync(new AppNotification
            {
                Title = "Scheduled service change completed",
                Message = $"{names} {(schedule.Action == AdGuardServiceScheduleAction.Allow ? "is now allowed" : "is now blocked")}.",
                Severity = NotificationSeverity.Success, Category = NotificationCategory.AdGuard,
                DeduplicationKey = $"AdGuardSchedule:{schedule.Id}:{occurrenceUtc.UtcTicks}:{(runNow ? "manual" : "scheduled")}" 
            });
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            schedule.LastError = "RouterPilot could not update the scheduled AdGuard services.";
            schedule.LastErrorUtc = _clock.UtcNow;
            await RefreshItemAsync(schedule);
            await SaveAsync();
            await _notifications.AddAsync(new AppNotification
            {
                Title = "Scheduled service change failed",
                Message = "RouterPilot could not update the scheduled AdGuard services.",
                Severity = NotificationSeverity.Warning, Category = NotificationCategory.AdGuard,
                DeduplicationKey = $"AdGuardScheduleFailed:{schedule.Id}:{occurrenceUtc.UtcTicks}"
            });
            if (runNow) throw;
        }
    }

    public async Task FlushAsync(CancellationToken token = default) => await SaveAsync(token);

    private async Task SaveAsync(CancellationToken token = default)
    {
        if (_disposed) return;
        await _saveGate.WaitAsync(token);
        try
        {
            List<AdGuardServiceSchedule> snapshot = await _dispatcher.InvokeAsync(() => _items.ToList());
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(snapshot, JsonOptions), token);
            File.Move(temp, _path, true);
        }
        finally { _saveGate.Release(); }
    }

    private async Task RefreshItemAsync(AdGuardServiceSchedule schedule) => await _dispatcher.InvokeAsync(() =>
    {
        int index = _items.IndexOf(schedule);
        if (index >= 0) _items[index] = schedule;
    });

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        _disposing = true;
        await _evaluationGate.WaitAsync();
        try { await FlushAsync(); }
        finally
        {
            _disposed = true;
            _evaluationGate.Release();
            _evaluationGate.Dispose();
            _saveGate.Dispose();
        }
    }

    private static void Normalize(AdGuardServiceSchedule item) => item.ServiceIds = item.ServiceIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private static AdGuardServiceSchedule Clone(AdGuardServiceSchedule s) => new() { Id = s.Id, GroupId = s.GroupId, Name = s.Name, ServiceIds = [.. s.ServiceIds], Action = s.Action, LocalTime = s.LocalTime, Recurrence = s.Recurrence, SelectedDays = s.SelectedDays, OneTimeDate = s.OneTimeDate, IsEnabled = s.IsEnabled, CreatedUtc = s.CreatedUtc, ServiceDisplay = s.ServiceDisplay };
    private static ScheduleDays ShiftDays(ScheduleDays days) { ScheduleDays shifted = ScheduleDays.None; foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>()) { ScheduleDays flag = day switch { DayOfWeek.Monday => ScheduleDays.Monday, DayOfWeek.Tuesday => ScheduleDays.Tuesday, DayOfWeek.Wednesday => ScheduleDays.Wednesday, DayOfWeek.Thursday => ScheduleDays.Thursday, DayOfWeek.Friday => ScheduleDays.Friday, DayOfWeek.Saturday => ScheduleDays.Saturday, _ => ScheduleDays.Sunday }; if ((days & flag) != 0) shifted |= day switch { DayOfWeek.Monday => ScheduleDays.Tuesday, DayOfWeek.Tuesday => ScheduleDays.Wednesday, DayOfWeek.Wednesday => ScheduleDays.Thursday, DayOfWeek.Thursday => ScheduleDays.Friday, DayOfWeek.Friday => ScheduleDays.Saturday, DayOfWeek.Saturday => ScheduleDays.Sunday, _ => ScheduleDays.Monday }; } return shifted; }
}
