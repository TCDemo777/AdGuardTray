using System.Windows;
using System.Windows.Controls;
using AdGuardTray.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AdGuardTray.Views;

public partial class TimelineView : UserControl
{
    private readonly TimelineViewModel _viewModel;

    public TimelineView()
    {
        InitializeComponent();
        _viewModel = ((App)Application.Current).Services
            .GetRequiredService<TimelineViewModel>();
        DataContext = _viewModel;
        Loaded += TimelineView_Loaded;
    }

    private async void TimelineView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= TimelineView_Loaded;
        await _viewModel.InitializeAsync();
    }
}
