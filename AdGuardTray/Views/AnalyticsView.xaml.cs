using System.Windows.Controls;
using AdGuardTray.ViewModels;

namespace AdGuardTray.Views
{
    public partial class AnalyticsView : UserControl
    {
        public AnalyticsView()
        {
            InitializeComponent();
            Loaded += AnalyticsView_Loaded;
        }

        private async void AnalyticsView_Loaded(
            object sender,
            System.Windows.RoutedEventArgs e)
        {
            if (DataContext is DashboardViewModel viewModel)
            {
                await viewModel.LoadWanHistoryAsync(
                    viewModel.SelectedWanHistoryRange);
            }
        }
    }
}
