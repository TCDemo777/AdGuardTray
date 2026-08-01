using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using AdGuardTray.Models;
using AdGuardTray.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdGuardTray.ViewModels;

public partial class NotificationCentreViewModel : ObservableObject
{
    private readonly NotificationService _notificationService;

    public NotificationCentreViewModel(NotificationService notificationService)
    {
        _notificationService = notificationService;
        Notifications = notificationService.Notifications;
        NotificationsView = CollectionViewSource.GetDefaultView(Notifications);
        NotificationsView.Filter = MatchesFilter;
        _notificationService.PropertyChanged += NotificationService_PropertyChanged;
    }

    public ObservableCollection<AppNotification> Notifications { get; }

    public ICollectionView NotificationsView { get; }

    public string[] Filters { get; } =
        { "All", "Unread", "Information", "Warning", "Error" };

    public int UnreadCount => _notificationService.UnreadCount;

    [ObservableProperty]
    private string selectedFilter = "All";

    partial void OnSelectedFilterChanged(string value) => NotificationsView.Refresh();

    [RelayCommand]
    private Task MarkAllReadAsync() => _notificationService.MarkAllReadAsync();

    [RelayCommand]
    private Task ClearAllAsync() => _notificationService.ClearAllAsync();

    [RelayCommand]
    private Task MarkReadAsync(AppNotification? notification) =>
        _notificationService.MarkReadAsync(notification);

    [RelayCommand]
    private Task RemoveAsync(AppNotification? notification) =>
        _notificationService.RemoveAsync(notification);

    private bool MatchesFilter(object item)
    {
        if (item is not AppNotification notification)
            return false;

        return SelectedFilter switch
        {
            "Unread" => !notification.IsRead,
            "Information" => notification.Severity == NotificationSeverity.Information,
            "Warning" => notification.Severity == NotificationSeverity.Warning,
            "Error" => notification.Severity == NotificationSeverity.Error,
            _ => true
        };
    }

    private void NotificationService_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationService.UnreadCount))
        {
            OnPropertyChanged(nameof(UnreadCount));
            NotificationsView.Refresh();
        }
    }
}
