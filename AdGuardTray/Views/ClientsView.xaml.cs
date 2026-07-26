using System;
using System.Windows;
using System.Windows.Controls;
using AdGuardTray.ViewModels;

namespace AdGuardTray.Views
{
    public partial class ClientsView : UserControl
    {
        private readonly ClientsViewModel _viewModel;

        public ClientsView()
        {
            InitializeComponent();

            _viewModel =
                new ClientsViewModel();

            DataContext =
                _viewModel;

            Loaded +=
                ClientsView_Loaded;
        }

        private async void ClientsView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            Loaded -=
                ClientsView_Loaded;

            try
            {
                await _viewModel
                    .LoadClientsAsync();
            }
            catch (Exception ex)
            {
                _viewModel.StatusMessage =
                    "Unable to load clients: " +
                    ex.Message;
            }
        }
    }
}