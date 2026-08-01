using System.Collections.ObjectModel;
using AdGuardTray.Models;
using AdGuardTray.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdGuardTray.ViewModels;

public partial class TimelineViewModel : ObservableObject
{
    private const int PageSize = 50;
    private readonly TimelineService _timelineService;
    private readonly List<TimelineEvent> _loadedEvents = new();

    public ObservableCollection<TimelineEvent> Events { get; } = new();
    public IReadOnlyList<TimelineFilter> Filters { get; } =
        Enum.GetValues<TimelineFilter>();

    [ObservableProperty] private TimelineFilter selectedFilter;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool hasMore = true;
    [ObservableProperty] private string statusMessage = string.Empty;

    public IAsyncRelayCommand LoadMoreCommand { get; }

    public TimelineViewModel(TimelineService timelineService)
    {
        _timelineService = timelineService;
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, () => HasMore && !IsLoading);
    }

    public async Task InitializeAsync()
    {
        if (_loadedEvents.Count == 0)
            await LoadMoreAsync();
    }

    private async Task LoadMoreAsync()
    {
        if (IsLoading || !HasMore) return;
        IsLoading = true;
        LoadMoreCommand.NotifyCanExecuteChanged();
        try
        {
            IReadOnlyList<TimelineEvent> page = await _timelineService
                .GetEventsAsync(_loadedEvents.Count, PageSize);
            foreach (TimelineEvent item in page)
                if (_loadedEvents.All(existing => existing.SourceId != item.SourceId))
                    _loadedEvents.Add(item);
            HasMore = page.Count == PageSize;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusMessage = "Unable to load timeline: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
            LoadMoreCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnSelectedFilterChanged(TimelineFilter value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnHasMoreChanged(bool value) => LoadMoreCommand.NotifyCanExecuteChanged();

    private void ApplyFilter()
    {
        IEnumerable<TimelineEvent> query = _loadedEvents;
        if (SelectedFilter != TimelineFilter.All)
            query = query.Where(item => item.Category.ToString() == SelectedFilter.ToString());
        string search = SearchText.Trim();
        if (search.Length > 0)
            query = query.Where(item =>
                item.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                item.Category.ToString().Contains(search, StringComparison.OrdinalIgnoreCase));

        Events.Clear();
        foreach (TimelineEvent item in query)
            Events.Add(item);
        StatusMessage = Events.Count == 0
            ? "No timeline events match the current filter."
            : $"Showing {Events.Count} events.";
    }
}
