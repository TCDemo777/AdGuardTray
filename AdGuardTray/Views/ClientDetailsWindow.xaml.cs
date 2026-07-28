using System;
using System.Windows;
using AdGuardTray.Models;
using AdGuardTray.ViewModels;

namespace AdGuardTray.Views
{
    public partial class ClientDetailsWindow : Window
    {
        private readonly ClientDetailsViewModel _viewModel;

        public ClientDetailsWindow(
            ClientInfo client)
        {
            InitializeComponent();

            _viewModel =
                new ClientDetailsViewModel(client);

            DataContext = _viewModel;

            Loaded += ClientDetailsWindow_Loaded;
            Closed += ClientDetailsWindow_Closed;
        }

        private async void ClientDetailsWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await _viewModel.StartAsync();
        }

        private void ClientDetailsWindow_Closed(
            object? sender,
            EventArgs e)
        {
            _viewModel.Stop();
        }
    }
}
