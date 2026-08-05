using System;
using System.Windows;
using RouterPilot.Models;
using RouterPilot.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace RouterPilot.Views
{
    public partial class ClientDetailsWindow : Window
    {
        private readonly ClientDetailsViewModel _viewModel;

        public ClientDetailsWindow(
            ClientInfo client)
        {
            InitializeComponent();

            _viewModel =
                ActivatorUtilities.CreateInstance<ClientDetailsViewModel>(
                    ((App)Application.Current).Services,
                    client);

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
