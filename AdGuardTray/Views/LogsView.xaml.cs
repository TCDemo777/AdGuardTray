using System.Windows;
using System.Windows.Controls;
using AdGuardTray.ViewModels;

namespace AdGuardTray.Views
{
    public partial class LogsView : UserControl
    {
        private readonly LogsViewModel _viewModel;

        public LogsView()
        {
            InitializeComponent();

            _viewModel =
                new LogsViewModel();

            DataContext =
                _viewModel;

            Loaded +=
                LogsView_Loaded;

            Unloaded +=
                LogsView_Unloaded;
        }

        private async void LogsView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await _viewModel
                .StartAsync();
        }

        private void LogsView_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            _viewModel.Stop();
        }
    }
}
