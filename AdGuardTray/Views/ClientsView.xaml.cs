using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AdGuardTray.Models;
using AdGuardTray.ViewModels;

namespace AdGuardTray.Views
{
    public partial class ClientsView : UserControl
    {
        private readonly ClientsViewModel _viewModel;
        private readonly DispatcherTimer _refreshTimer;

        public ClientsView()
        {
            InitializeComponent();

            _viewModel = new ClientsViewModel();
            DataContext = _viewModel;

            _refreshTimer =
                new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };

            _refreshTimer.Tick +=
                ClientsRefreshTimer_Tick;

            Loaded += ClientsView_Loaded;
            IsVisibleChanged += ClientsView_IsVisibleChanged;
        }

        private async void ClientsView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (IsVisible)
            {
                await RefreshClientsAsync();
                StartRefreshTimer();
            }
        }

        private async void ClientsView_IsVisibleChanged(
            object sender,
            DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                await RefreshClientsAsync();
                StartRefreshTimer();
            }
            else
            {
                _refreshTimer.Stop();
            }
        }

        private async void ClientsRefreshTimer_Tick(
            object? sender,
            EventArgs e)
        {
            if (!IsVisible ||
                _viewModel.IsLoading)
            {
                return;
            }

            await RefreshClientsAsync();
        }

        private void StartRefreshTimer()
        {
            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }
        }

        private async System.Threading.Tasks.Task RefreshClientsAsync()
        {
            try
            {
                await _viewModel.LoadClientsAsync();

                if (!_viewModel.StatusMessage.StartsWith(
                        "Unable",
                        StringComparison.OrdinalIgnoreCase))
                {
                    _viewModel.StatusMessage +=
                        " · updated " +
                        DateTime.Now.ToString("HH:mm:ss");
                }
            }
            catch (Exception ex)
            {
                _viewModel.StatusMessage =
                    "Unable to load clients: " +
                    ex.Message;
            }
        }

        private void SortOptionComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count == 0 ||
                e.AddedItems[0] is not string selectedSort)
            {
                return;
            }

            // Run after WPF finishes changing the selection. This removes
            // the previous requirement to press Ascending/Descending.
            Dispatcher.BeginInvoke(
                new Action(() =>
                    _viewModel.SelectSortOption(selectedSort)),
                DispatcherPriority.DataBind);
        }

        private void FavoriteButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.Tag is ClientInfo client)
            {
                _viewModel.ToggleFavorite(client);
                e.Handled = true;
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
