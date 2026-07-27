using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AdGuardTray.Models;
using AdGuardTray.ViewModels;

namespace AdGuardTray.Views
{
    public partial class ClientsView : UserControl
    {
        private readonly ClientsViewModel _viewModel;

        public ClientsView()
        {
            InitializeComponent();

            _viewModel = new ClientsViewModel();
            DataContext = _viewModel;

            Loaded += ClientsView_Loaded;
        }

        private async void ClientsView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            Loaded -= ClientsView_Loaded;

            try
            {
                await _viewModel.LoadClientsAsync();
            }
            catch (Exception ex)
            {
                _viewModel.StatusMessage =
                    "Unable to load clients: " +
                    ex.Message;
            }
        }

        private void ViewDetailsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OpenSelectedClient();
        }

        private void ClientsGrid_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            OpenSelectedClient();
        }

        private void OpenSelectedClient()
        {
            ClientInfo? client =
                _viewModel.SelectedClient;

            if (client is null)
            {
                _viewModel.StatusMessage =
                    "Select a client first.";
                return;
            }

            var window =
                new ClientDetailsWindow(client)
                {
                    Owner = Window.GetWindow(this)
                };

            window.ShowDialog();
        }
    }
}
