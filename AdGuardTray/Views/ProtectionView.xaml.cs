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

        private void AddSchedule_Click(object sender, RoutedEventArgs e)
        {
            AllowedWindowEditor.IsExpanded = false;
            ScheduleEditor.IsExpanded = true;
            ScheduleEditor.Focus();
        }

        private void OpenAllowedWindow_Click(object sender, RoutedEventArgs e)
        {
            ScheduleEditor.IsExpanded = false;
            AllowedWindowEditor.IsExpanded = true;
            AllowedWindowEditor.Focus();
        }

        private void EditWindow_Click(object sender, RoutedEventArgs e)
        {
            ScheduleEditor.IsExpanded = false;
            AllowedWindowEditor.IsExpanded = true;
            AllowedWindowEditor.Focus();
        }

        private void EditSchedule_Click(object sender, RoutedEventArgs e)
        {
            AllowedWindowEditor.IsExpanded = false;
            ScheduleEditor.IsExpanded = true;
            ScheduleEditor.Focus();
        }

        private void CancelSchedule_Click(object sender, RoutedEventArgs e) =>
            ScheduleEditor.IsExpanded = false;

        private void CancelWindow_Click(object sender, RoutedEventArgs e) =>
            AllowedWindowEditor.IsExpanded = false;

        private void RunWindowAllowNow_Click(object sender, RoutedEventArgs e) =>
            ConfirmWindowAction(sender, Models.AdGuardServiceScheduleAction.Allow);

        private void RunWindowBlockNow_Click(object sender, RoutedEventArgs e) =>
            ConfirmWindowAction(sender, Models.AdGuardServiceScheduleAction.Block);

        private void WindowMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.ContextMenu is null) return;
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }

        private void DuplicateWindow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is Models.AdGuardServiceWindow window)
                _viewModel.Schedules.DuplicateWindowCommand.Execute(window);
        }

        private void ConfirmWindowAction(object sender, Models.AdGuardServiceScheduleAction action)
        {
            if ((sender as FrameworkElement)?.DataContext is not Models.AdGuardServiceWindow window) return;
            if (MessageBox.Show($"{action} services in '{window.Name}' now?", "RouterPilot", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (action == Models.AdGuardServiceScheduleAction.Allow)
                _viewModel.Schedules.RunAllowNowCommand.Execute(window);
            else
                _viewModel.Schedules.RunBlockNowCommand.Execute(window);
        }

        private void DeleteWindow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not Models.AdGuardServiceWindow window) return;
            if (MessageBox.Show($"Delete the complete allowed-time window '{window.Name}'?", "RouterPilot", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                _viewModel.Schedules.DeleteWindowCommand.Execute(window);
        }

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
