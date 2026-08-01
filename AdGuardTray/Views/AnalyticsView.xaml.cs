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
                await System.Threading.Tasks.Task.WhenAll(
                    viewModel.LoadWanHistoryAsync(
                        viewModel.SelectedWanHistoryRange),
                    viewModel.LoadRouterHealthHistoryAsync(
                        viewModel.SelectedRouterHealthHistoryRange));
            }
        }
    }
}
