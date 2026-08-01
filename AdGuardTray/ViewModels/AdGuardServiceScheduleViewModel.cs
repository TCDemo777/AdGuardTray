using System.Collections.ObjectModel;
using AdGuardTray.Models;
using AdGuardTray.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdGuardTray.ViewModels;

public sealed partial class ScheduleServiceOption : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    [ObservableProperty] private bool isSelected;
}

public sealed partial class AdGuardServiceScheduleViewModel : ObservableObject
{
    private readonly AdGuardServiceScheduleService _service;
    [ObservableProperty] private AdGuardServiceSchedule? selectedSchedule;
    [ObservableProperty] private string editorName = string.Empty;
    [ObservableProperty] private string editorTime = "16:00";
    [ObservableProperty] private AdGuardServiceScheduleAction editorAction;
    [ObservableProperty] private AdGuardServiceScheduleRecurrence editorRecurrence = AdGuardServiceScheduleRecurrence.Daily;
    [ObservableProperty] private DateTime? editorOneTimeDate = DateTime.Today;
    [ObservableProperty] private ScheduleDays editorDays = ScheduleDays.Weekdays;
    [ObservableProperty] private string status = string.Empty;
    [ObservableProperty] private bool isExecuting;
    [ObservableProperty] private ScheduleServiceOption? windowService;
    [ObservableProperty] private string windowAllowTime = "16:00";
    [ObservableProperty] private string windowBlockTime = "19:30";

    public AdGuardServiceScheduleViewModel(AdGuardServiceScheduleService service)
    {
        _service = service;
        Schedules = service.Schedules;
        Actions = Enum.GetValues<AdGuardServiceScheduleAction>();
        Recurrences = Enum.GetValues<AdGuardServiceScheduleRecurrence>();
    }

    public ReadOnlyObservableCollection<AdGuardServiceSchedule> Schedules { get; }
    public ObservableCollection<ScheduleServiceOption> AvailableServices { get; } = [];
    public AdGuardServiceScheduleAction[] Actions { get; }
    public AdGuardServiceScheduleRecurrence[] Recurrences { get; }
    public bool IsOnce => EditorRecurrence == AdGuardServiceScheduleRecurrence.Once;
    public bool IsSelectedDays => EditorRecurrence == AdGuardServiceScheduleRecurrence.SelectedDays;
    public bool Monday { get => Has(ScheduleDays.Monday); set => SetDay(ScheduleDays.Monday, value); }
    public bool Tuesday { get => Has(ScheduleDays.Tuesday); set => SetDay(ScheduleDays.Tuesday, value); }
    public bool Wednesday { get => Has(ScheduleDays.Wednesday); set => SetDay(ScheduleDays.Wednesday, value); }
    public bool Thursday { get => Has(ScheduleDays.Thursday); set => SetDay(ScheduleDays.Thursday, value); }
    public bool Friday { get => Has(ScheduleDays.Friday); set => SetDay(ScheduleDays.Friday, value); }
    public bool Saturday { get => Has(ScheduleDays.Saturday); set => SetDay(ScheduleDays.Saturday, value); }
    public bool Sunday { get => Has(ScheduleDays.Sunday); set => SetDay(ScheduleDays.Sunday, value); }

    partial void OnEditorRecurrenceChanged(AdGuardServiceScheduleRecurrence value) { OnPropertyChanged(nameof(IsOnce)); OnPropertyChanged(nameof(IsSelectedDays)); }
    partial void OnEditorDaysChanged(ScheduleDays value) { foreach (string name in new[] { nameof(Monday), nameof(Tuesday), nameof(Wednesday), nameof(Thursday), nameof(Friday), nameof(Saturday), nameof(Sunday) }) OnPropertyChanged(name); }
    private bool Has(ScheduleDays day) => (EditorDays & day) != 0;
    private void SetDay(ScheduleDays day, bool enabled) => EditorDays = enabled ? EditorDays | day : EditorDays & ~day;

    public void SetAvailableServices(IEnumerable<BlockedServiceItem> services)
    {
        HashSet<string> selected = AvailableServices.Where(x => x.IsSelected).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AvailableServices.Clear();
        foreach (BlockedServiceItem item in services.OrderBy(x => x.Name)) AvailableServices.Add(new() { Id = item.Id, Name = item.Name, IsSelected = selected.Contains(item.Id) });
        WindowService ??= AvailableServices.FirstOrDefault();
        foreach (AdGuardServiceSchedule schedule in Schedules) schedule.ServiceDisplay = string.Join(", ", schedule.ServiceIds.Select(id => AvailableServices.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id));
    }

    [RelayCommand]
    private void Edit(AdGuardServiceSchedule schedule)
    {
        SelectedSchedule = schedule; EditorName = schedule.Name; EditorTime = schedule.LocalTime.ToString("HH:mm");
        EditorAction = schedule.Action; EditorRecurrence = schedule.Recurrence; EditorDays = schedule.SelectedDays;
        EditorOneTimeDate = schedule.OneTimeDate?.ToDateTime(TimeOnly.MinValue);
        foreach (ScheduleServiceOption option in AvailableServices) option.IsSelected = schedule.ServiceIds.Contains(option.Id, StringComparer.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void NewSchedule()
    {
        SelectedSchedule = null; EditorName = string.Empty; EditorTime = "16:00"; EditorAction = AdGuardServiceScheduleAction.Allow;
        foreach (ScheduleServiceOption option in AvailableServices) option.IsSelected = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!TimeOnly.TryParse(EditorTime, out TimeOnly time)) { Status = "Enter a valid time, for example 16:00."; return; }
        List<string> ids = AvailableServices.Where(x => x.IsSelected).Select(x => x.Id).ToList();
        if (ids.Count == 0) { Status = "Select at least one service."; return; }
        if (string.IsNullOrWhiteSpace(EditorName)) { Status = "Enter a schedule name."; return; }
        if (EditorRecurrence == AdGuardServiceScheduleRecurrence.Once && EditorOneTimeDate is null) { Status = "Choose a date for a one-time schedule."; return; }
        if (EditorRecurrence == AdGuardServiceScheduleRecurrence.SelectedDays && EditorDays == ScheduleDays.None) { Status = "Choose at least one day."; return; }
        AdGuardServiceSchedule item = SelectedSchedule ?? new();
        item.Name = EditorName.Trim(); item.LocalTime = time; item.Action = EditorAction; item.Recurrence = EditorRecurrence;
        item.SelectedDays = EditorDays; item.OneTimeDate = EditorOneTimeDate is null ? null : DateOnly.FromDateTime(EditorOneTimeDate.Value); item.ServiceIds = ids;
        item.ServiceDisplay = string.Join(", ", AvailableServices.Where(x => x.IsSelected).Select(x => x.Name));
        await _service.SaveScheduleAsync(item); SelectedSchedule = item; Status = "Schedule saved.";
    }

    [RelayCommand] private async Task DeleteAsync(AdGuardServiceSchedule schedule) { await _service.DeleteAsync(schedule.Id); Status = "Schedule deleted."; }
    [RelayCommand] private async Task DuplicateAsync(AdGuardServiceSchedule schedule) { await _service.DuplicateAsync(schedule); Status = "Schedule duplicated."; }
    [RelayCommand] private async Task ToggleAsync(AdGuardServiceSchedule schedule) { schedule.IsEnabled = !schedule.IsEnabled; await _service.SaveScheduleAsync(schedule); Status = schedule.IsEnabled ? "Schedule enabled." : "Schedule disabled."; }

    [RelayCommand]
    private async Task RunNowAsync(AdGuardServiceSchedule schedule)
    {
        if (IsExecuting) return; IsExecuting = true;
        try { await _service.RunNowAsync(schedule); Status = "Scheduled service action completed."; }
        catch (Exception) { Status = "RouterPilot could not update the scheduled AdGuard services."; }
        finally { IsExecuting = false; }
    }

    [RelayCommand]
    private async Task CreateWindowAsync()
    {
        if (WindowService is null || !TimeOnly.TryParse(WindowAllowTime, out TimeOnly allow) || !TimeOnly.TryParse(WindowBlockTime, out TimeOnly block)) { Status = "Choose a service and enter valid allow/block times."; return; }
        await _service.CreateAllowedWindowAsync(WindowService.Id, WindowService.Name, allow, block, EditorDays);
        Status = "Allowed-time window created.";
    }
}
