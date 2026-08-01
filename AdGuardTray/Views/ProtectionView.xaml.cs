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
    }
}
