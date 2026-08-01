using System.Windows;
using System.Windows.Controls;
using AdGuardTray.ViewModels;
using AdGuardTray.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AdGuardTray.Views
{
    public partial class ProtectionView : UserControl
    {
        private readonly ProtectionViewModel _viewModel;
        public ProtectionView()
        {
            InitializeComponent();
            _viewModel = ((App)Application.Current).Services
                .GetRequiredService<ProtectionViewModel>();
            DataContext = _viewModel;
            Loaded += ProtectionView_Loaded;
            Unloaded += ProtectionView_Unloaded;
        }
        private async void ProtectionView_Loaded(object sender, RoutedEventArgs e) => await _viewModel.StartAsync();
        private void ProtectionView_Unloaded(object sender, RoutedEventArgs e) => _viewModel.Stop();

        private void RunScheduleNow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not Models.AdGuardServiceSchedule schedule) return;
            if (MessageBox.Show($"Run '{schedule.Name}' now?", "RouterPilot", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                _viewModel.Schedules.RunNowCommand.Execute(schedule);
        }

        private void DeleteSchedule_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not Models.AdGuardServiceSchedule schedule) return;
            if (MessageBox.Show($"Delete '{schedule.Name}'?", "RouterPilot", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                _viewModel.Schedules.DeleteCommand.Execute(schedule);
        }
    }
}
