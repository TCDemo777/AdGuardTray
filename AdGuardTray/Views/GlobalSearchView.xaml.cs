using System.Windows;
using System.Windows.Controls;
using AdGuardTray.ViewModels;

namespace AdGuardTray.Views
{
    public partial class GlobalSearchView : UserControl
    {
        private readonly GlobalSearchViewModel _viewModel;

        public GlobalSearchView()
        {
            InitializeComponent();

            _viewModel =
                new GlobalSearchViewModel();

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
