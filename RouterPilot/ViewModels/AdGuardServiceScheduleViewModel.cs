using System.Collections.ObjectModel;
using RouterPilot.Models;
using RouterPilot.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;

namespace RouterPilot.ViewModels;

public sealed partial class ScheduleServiceOption : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string IconSvg { get; init; } = string.Empty;
    [ObservableProperty] private bool isSelected;
}

public sealed partial class AdGuardServiceScheduleViewModel : ObservableObject
{
    private readonly AdGuardServiceScheduleService _service;
    private readonly IAdGuardServiceCatalogueProvider _catalogueProvider;
    [ObservableProperty] private AdGuardServiceSchedule? selectedSchedule;
    [ObservableProperty] private AdGuardServiceWindow? selectedWindow;
    [ObservableProperty] private string editorName = string.Empty;
    [ObservableProperty] private string editorTime = "16:00";
    [ObservableProperty] private AdGuardServiceScheduleAction editorAction;
    [ObservableProperty] private AdGuardServiceScheduleRecurrence editorRecurrence = AdGuardServiceScheduleRecurrence.Daily;
    [ObservableProperty] private DateTime? editorOneTimeDate = DateTime.Today;
    [ObservableProperty] private ScheduleDays editorDays = ScheduleDays.Weekdays;
    [ObservableProperty] private bool editorIsEnabled = true;
    [ObservableProperty] private string windowName = string.Empty;
    [ObservableProperty] private string windowAllowTime = "16:00";
    [ObservableProperty] private string windowBlockTime = "19:30";
    [ObservableProperty] private AdGuardServiceScheduleRecurrence windowRecurrence = AdGuardServiceScheduleRecurrence.SelectedDays;
    [ObservableProperty] private DateTime? windowOneTimeDate = DateTime.Today;
    [ObservableProperty] private ScheduleDays windowDays = ScheduleDays.Weekdays;
    [ObservableProperty] private bool windowIsEnabled = true;
    [ObservableProperty] private string windowServiceSearch = string.Empty;
    [ObservableProperty] private string status = string.Empty;
    [ObservableProperty] private bool isExecuting;

    public AdGuardServiceScheduleViewModel(AdGuardServiceScheduleService service, IAdGuardServiceCatalogueProvider catalogueProvider)
    {
        _service = service;
        _catalogueProvider = catalogueProvider;
        Schedules = service.Schedules;
        Windows = service.Windows;
        AdvancedSchedules = service.AdvancedSchedules;
        Actions = Enum.GetValues<AdGuardServiceScheduleAction>();
        Recurrences = Enum.GetValues<AdGuardServiceScheduleRecurrence>();
        WindowServicesView = new ListCollectionView(AvailableServices) { Filter = FilterWindowService };
        catalogueProvider.CatalogueChanged += (_, _) => SetAvailableServices(catalogueProvider.Services);
        SetAvailableServices(catalogueProvider.Services);
    }

    public ReadOnlyObservableCollection<AdGuardServiceSchedule> Schedules { get; }
    public ReadOnlyObservableCollection<AdGuardServiceWindow> Windows { get; }
    public ReadOnlyObservableCollection<AdGuardServiceSchedule> AdvancedSchedules { get; }
    public ObservableCollection<ScheduleServiceOption> AvailableServices { get; } = [];
    public ICollectionView WindowServicesView { get; }
    public AdGuardServiceScheduleAction[] Actions { get; }
    public AdGuardServiceScheduleRecurrence[] Recurrences { get; }
    public bool IsOnce => EditorRecurrence == AdGuardServiceScheduleRecurrence.Once;
    public bool IsSelectedDays => EditorRecurrence == AdGuardServiceScheduleRecurrence.SelectedDays;
    public bool IsWindowOnce => WindowRecurrence == AdGuardServiceScheduleRecurrence.Once;
    public bool IsWindowSelectedDays => WindowRecurrence == AdGuardServiceScheduleRecurrence.SelectedDays;
    public string SelectedServicesSummary
    {
        get
        {
            string[] names = AvailableServices.Where(option => option.IsSelected).Select(option => option.Name).ToArray();
            return names.Length == 0 ? "No services selected" : $"{names.Length} selected: {string.Join(", ", names)}";
        }
    }
    public bool Monday { get => Has(EditorDays, ScheduleDays.Monday); set => SetEditorDay(ScheduleDays.Monday, value); }
    public bool Tuesday { get => Has(EditorDays, ScheduleDays.Tuesday); set => SetEditorDay(ScheduleDays.Tuesday, value); }
    public bool Wednesday { get => Has(EditorDays, ScheduleDays.Wednesday); set => SetEditorDay(ScheduleDays.Wednesday, value); }
    public bool Thursday { get => Has(EditorDays, ScheduleDays.Thursday); set => SetEditorDay(ScheduleDays.Thursday, value); }
    public bool Friday { get => Has(EditorDays, ScheduleDays.Friday); set => SetEditorDay(ScheduleDays.Friday, value); }
    public bool Saturday { get => Has(EditorDays, ScheduleDays.Saturday); set => SetEditorDay(ScheduleDays.Saturday, value); }
    public bool Sunday { get => Has(EditorDays, ScheduleDays.Sunday); set => SetEditorDay(ScheduleDays.Sunday, value); }
    public bool WindowMonday { get => Has(WindowDays, ScheduleDays.Monday); set => SetWindowDay(ScheduleDays.Monday, value); }
    public bool WindowTuesday { get => Has(WindowDays, ScheduleDays.Tuesday); set => SetWindowDay(ScheduleDays.Tuesday, value); }
    public bool WindowWednesday { get => Has(WindowDays, ScheduleDays.Wednesday); set => SetWindowDay(ScheduleDays.Wednesday, value); }
    public bool WindowThursday { get => Has(WindowDays, ScheduleDays.Thursday); set => SetWindowDay(ScheduleDays.Thursday, value); }
    public bool WindowFriday { get => Has(WindowDays, ScheduleDays.Friday); set => SetWindowDay(ScheduleDays.Friday, value); }
    public bool WindowSaturday { get => Has(WindowDays, ScheduleDays.Saturday); set => SetWindowDay(ScheduleDays.Saturday, value); }
    public bool WindowSunday { get => Has(WindowDays, ScheduleDays.Sunday); set => SetWindowDay(ScheduleDays.Sunday, value); }

    partial void OnEditorRecurrenceChanged(AdGuardServiceScheduleRecurrence value) { OnPropertyChanged(nameof(IsOnce)); OnPropertyChanged(nameof(IsSelectedDays)); }
    partial void OnWindowRecurrenceChanged(AdGuardServiceScheduleRecurrence value) { OnPropertyChanged(nameof(IsWindowOnce)); OnPropertyChanged(nameof(IsWindowSelectedDays)); }
    partial void OnEditorDaysChanged(ScheduleDays value) => NotifyDayProperties(false);
    partial void OnWindowDaysChanged(ScheduleDays value) => NotifyDayProperties(true);
    partial void OnWindowServiceSearchChanged(string value) => WindowServicesView.Refresh();

    public void SetAvailableServices(IEnumerable<BlockedServiceItem> services)
    {
        HashSet<string> selected = AvailableServices.Where(x => x.IsSelected).Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AvailableServices.Clear();
        foreach (BlockedServiceItem item in services.OrderBy(x => x.Name))
        {
            var option = new ScheduleServiceOption { Id = item.Id, Name = item.Name, Category = item.Category, IconSvg = item.IconSvg, IsSelected = selected.Contains(item.Id) };
            option.PropertyChanged += ServiceOption_PropertyChanged;
            AvailableServices.Add(option);
        }
        WindowServicesView.Refresh();
        OnPropertyChanged(nameof(SelectedServicesSummary));
        foreach (AdGuardServiceSchedule schedule in Schedules) schedule.ServiceDisplay = DisplayServices(schedule.ServiceIds);
        foreach (AdGuardServiceWindow window in Windows) window.ServiceDisplay = DisplayServices(window.ServiceIds);
    }

    [RelayCommand]
    private void EditWindow(AdGuardServiceWindow window)
    {
        SelectedWindow = window; WindowName = window.Name; WindowAllowTime = window.AllowTime.ToString("HH:mm"); WindowBlockTime = window.BlockTime.ToString("HH:mm");
        WindowRecurrence = window.Recurrence; WindowDays = window.SelectedDays; WindowOneTimeDate = window.OneTimeDate?.ToDateTime(TimeOnly.MinValue); WindowIsEnabled = window.IsEnabled;
        SelectServices(window.ServiceIds); Status = string.Empty;
        Debug.Assert(AvailableServices.Count(option => option.IsSelected) == window.ServiceIds.Distinct(StringComparer.OrdinalIgnoreCase).Count(id => AvailableServices.Any(option => option.Id.Equals(id, StringComparison.OrdinalIgnoreCase))));
    }

    [RelayCommand]
    private void NewWindow()
    {
        SelectedWindow = null; WindowName = string.Empty; WindowAllowTime = "16:00"; WindowBlockTime = "19:30";
        WindowRecurrence = AdGuardServiceScheduleRecurrence.SelectedDays; WindowDays = ScheduleDays.Weekdays; WindowOneTimeDate = DateTime.Today; WindowIsEnabled = true;
        SelectServices([]); Status = string.Empty;
    }

    [RelayCommand]
    private async Task SaveWindowAsync()
    {
        if (!ValidateWindow(out TimeOnly allow, out TimeOnly block, out List<string> ids)) return;
        AdGuardServiceWindow item = SelectedWindow is null ? new() : CloneWindow(SelectedWindow);
        item.Name = WindowName.Trim(); item.ServiceIds = ids; item.AllowTime = allow; item.BlockTime = block;
        item.Recurrence = WindowRecurrence; item.SelectedDays = WindowDays;
        item.OneTimeDate = WindowOneTimeDate is null ? null : DateOnly.FromDateTime(WindowOneTimeDate.Value); item.IsEnabled = WindowIsEnabled;
        item.ServiceDisplay = DisplayServices(ids);
        try { await _service.SaveWindowAsync(item); SelectedWindow = item; Status = "Allowed-time window saved."; }
        catch (InvalidOperationException ex) { Status = ex.Message; }
    }

    [RelayCommand] private async Task DeleteWindowAsync(AdGuardServiceWindow window) { await _service.DeleteWindowAsync(window); Status = "Allowed-time window deleted."; }
    [RelayCommand] private async Task DuplicateWindowAsync(AdGuardServiceWindow window) { await _service.DuplicateWindowAsync(window); Status = "Allowed-time window duplicated."; }
    [RelayCommand] private async Task ToggleWindowAsync(AdGuardServiceWindow window) { await _service.SetWindowEnabledAsync(window, !window.IsEnabled); Status = window.IsEnabled ? "Allowed-time window disabled." : "Allowed-time window enabled."; }
    [RelayCommand] private Task RunAllowNowAsync(AdGuardServiceWindow window) => RunWindowNowAsync(window, AdGuardServiceScheduleAction.Allow);
    [RelayCommand] private Task RunBlockNowAsync(AdGuardServiceWindow window) => RunWindowNowAsync(window, AdGuardServiceScheduleAction.Block);

    private async Task RunWindowNowAsync(AdGuardServiceWindow window, AdGuardServiceScheduleAction action)
    {
        if (IsExecuting) return; IsExecuting = true;
        try { await _service.RunWindowNowAsync(window, action); Status = action == AdGuardServiceScheduleAction.Allow ? "Services are now allowed." : "Services are now blocked."; }
        catch (OperationCanceledException) { }
        catch { Status = "RouterPilot could not update the scheduled AdGuard services."; }
        finally { IsExecuting = false; }
    }

    [RelayCommand]
    private void Edit(AdGuardServiceSchedule schedule)
    {
        SelectedSchedule = schedule; EditorName = schedule.Name; EditorTime = schedule.LocalTime.ToString("HH:mm");
        EditorAction = schedule.Action; EditorRecurrence = schedule.Recurrence; EditorDays = schedule.SelectedDays; EditorIsEnabled = schedule.IsEnabled;
        EditorOneTimeDate = schedule.OneTimeDate?.ToDateTime(TimeOnly.MinValue); SelectServices(schedule.ServiceIds); Status = string.Empty;
        Debug.Assert(AvailableServices.Count(option => option.IsSelected) == schedule.ServiceIds.Distinct(StringComparer.OrdinalIgnoreCase).Count(id => AvailableServices.Any(option => option.Id.Equals(id, StringComparison.OrdinalIgnoreCase))));
    }

    [RelayCommand]
    private void NewSchedule()
    {
        SelectedSchedule = null; EditorName = string.Empty; EditorTime = "16:00"; EditorAction = AdGuardServiceScheduleAction.Allow;
        EditorRecurrence = AdGuardServiceScheduleRecurrence.Daily; EditorDays = ScheduleDays.Weekdays; EditorIsEnabled = true; SelectServices([]); Status = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!TimeOnly.TryParse(EditorTime, out TimeOnly time)) { Status = "Enter a valid time, for example 16:00."; return; }
        List<string> ids = SelectedServiceIds();
        if (ids.Count == 0) { Status = "Select at least one service."; return; }
        if (string.IsNullOrWhiteSpace(EditorName)) { Status = "Enter a schedule name."; return; }
        if (EditorRecurrence == AdGuardServiceScheduleRecurrence.Once && EditorOneTimeDate is null) { Status = "Choose a date for a one-time schedule."; return; }
        if (EditorRecurrence == AdGuardServiceScheduleRecurrence.SelectedDays && EditorDays == ScheduleDays.None) { Status = "Choose at least one day."; return; }
        AdGuardServiceSchedule item = SelectedSchedule ?? new();
        item.Name = EditorName.Trim(); item.LocalTime = time; item.Action = EditorAction; item.Recurrence = EditorRecurrence;
        item.SelectedDays = EditorDays; item.OneTimeDate = EditorOneTimeDate is null ? null : DateOnly.FromDateTime(EditorOneTimeDate.Value); item.IsEnabled = EditorIsEnabled; item.ServiceIds = ids; item.ServiceDisplay = DisplayServices(ids);
        await _service.SaveScheduleAsync(item); SelectedSchedule = item; Status = "Advanced schedule saved.";
    }

    [RelayCommand] private async Task DeleteAsync(AdGuardServiceSchedule schedule) { await _service.DeleteAsync(schedule.Id); Status = "Schedule deleted."; }
    [RelayCommand] private async Task DuplicateAsync(AdGuardServiceSchedule schedule) { await _service.DuplicateAsync(schedule); Status = "Schedule duplicated."; }
    [RelayCommand] private async Task ToggleAsync(AdGuardServiceSchedule schedule) { schedule.IsEnabled = !schedule.IsEnabled; await _service.SaveScheduleAsync(schedule); Status = schedule.IsEnabled ? "Schedule enabled." : "Schedule disabled."; }
    [RelayCommand] private async Task RunNowAsync(AdGuardServiceSchedule schedule) { if (IsExecuting) return; IsExecuting = true; try { await _service.RunNowAsync(schedule); Status = "Scheduled service action completed."; } catch { Status = "RouterPilot could not update the scheduled AdGuard services."; } finally { IsExecuting = false; } }

    private bool ValidateWindow(out TimeOnly allow, out TimeOnly block, out List<string> ids)
    {
        ids = SelectedServiceIds();
        bool validAllow = TimeOnly.TryParse(WindowAllowTime, out allow);
        bool validBlock = TimeOnly.TryParse(WindowBlockTime, out block);
        if (!validAllow || !validBlock) { Status = "Enter valid allow and block times, for example 16:00."; return false; }
        if (allow == block) { Status = "Allow and block times must be different."; return false; }
        if (string.IsNullOrWhiteSpace(WindowName)) { Status = "Enter a window name."; return false; }
        if (ids.Count == 0) { Status = "Select at least one service."; return false; }
        if (WindowRecurrence == AdGuardServiceScheduleRecurrence.Once && WindowOneTimeDate is null) { Status = "Choose a date for a one-time window."; return false; }
        if (WindowRecurrence == AdGuardServiceScheduleRecurrence.SelectedDays && WindowDays == ScheduleDays.None) { Status = "Choose at least one day."; return false; }
        return true;
    }

    private List<string> SelectedServiceIds() => AvailableServices.Where(x => x.IsSelected).Select(x => x.Id).ToList();
    private void SelectServices(IEnumerable<string> ids) { HashSet<string> set = ids.ToHashSet(StringComparer.OrdinalIgnoreCase); foreach (ScheduleServiceOption option in AvailableServices) option.IsSelected = set.Contains(option.Id); OnPropertyChanged(nameof(SelectedServicesSummary)); }
    private void ServiceOption_PropertyChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(ScheduleServiceOption.IsSelected)) OnPropertyChanged(nameof(SelectedServicesSummary)); }
    private string DisplayServices(IEnumerable<string> ids) => string.Join(", ", ids.Select(id => AvailableServices.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? id));
    private bool FilterWindowService(object item)
    {
        if (item is not ScheduleServiceOption service) return false;
        string search = WindowServiceSearch.Trim();
        return search.Length == 0 || service.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || service.Id.Contains(search, StringComparison.OrdinalIgnoreCase) || service.Category.Contains(search, StringComparison.OrdinalIgnoreCase);
    }
    private static bool Has(ScheduleDays value, ScheduleDays day) => (value & day) != 0;
    private void SetEditorDay(ScheduleDays day, bool enabled) => EditorDays = enabled ? EditorDays | day : EditorDays & ~day;
    private void SetWindowDay(ScheduleDays day, bool enabled) => WindowDays = enabled ? WindowDays | day : WindowDays & ~day;
    private void NotifyDayProperties(bool window) { foreach (string day in new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }) OnPropertyChanged(window ? $"Window{day}" : day); }
    private static AdGuardServiceWindow CloneWindow(AdGuardServiceWindow w) => new() { Id = w.Id, AllowScheduleId = w.AllowScheduleId, BlockScheduleId = w.BlockScheduleId, Name = w.Name, ServiceIds = [.. w.ServiceIds], AllowTime = w.AllowTime, BlockTime = w.BlockTime, Recurrence = w.Recurrence, SelectedDays = w.SelectedDays, OneTimeDate = w.OneTimeDate, IsEnabled = w.IsEnabled, CreatedUtc = w.CreatedUtc, ServiceDisplay = w.ServiceDisplay };
}
