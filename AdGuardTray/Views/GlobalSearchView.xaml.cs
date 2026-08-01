using System.Windows;
using System.Windows.Controls;
using AdGuardTray.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AdGuardTray.Views
{
    public partial class GlobalSearchView : UserControl
    {
        private readonly GlobalSearchViewModel _viewModel;

        public GlobalSearchView()
        {
            InitializeComponent();

            _viewModel =
                ((App)Application.Current).Services
                    .GetRequiredService<GlobalSearchViewModel>();

            DataContext =
                _viewModel;

            Loaded += GlobalSearchView_Loaded;
        }

        private async void GlobalSearchView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            Loaded -= GlobalSearchView_Loaded;
            await _viewModel.RefreshIndexAsync();
        }
    }
}
