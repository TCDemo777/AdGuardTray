using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AdGuardTray.Models;
using AdGuardTray.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdGuardTray.ViewModels
{
    public partial class ClientsViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;

        private readonly List<ClientInfo> _allClients =
            new();

        public ObservableCollection<ClientInfo> Clients { get; } =
            new();

        [ObservableProperty]
        private string searchText =
            string.Empty;

        [ObservableProperty]
        private ClientInfo? selectedClient;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string statusMessage =
            "No client data loaded.";

        public ClientsViewModel()
        {
            _settingsService =
                new SettingsService();
        }

        [RelayCommand]
        public async Task LoadClientsAsync()
        {
            if (IsLoading)
            {
                return;
            }

            IsLoading =
                true;

            StatusMessage =
                "Loading clients...";

            try
            {
                var settings =
                    _settingsService.Load();

                if (string.IsNullOrWhiteSpace(
                        settings.RouterIp) ||
                    string.IsNullOrWhiteSpace(
                        settings.Username))
                {
                    ClearClients();

                    StatusMessage =
                        "Router settings are incomplete.";

                    return;
                }

                string password =
                    _settingsService.DecryptPassword(
                        settings.EncryptedPassword);

                if (string.IsNullOrWhiteSpace(
                        password))
                {
                    ClearClients();

                    StatusMessage =
                        "The router password is missing.";

                    return;
                }

                var routerManager =
                    new RouterManager(
                        settings.RouterIp,
                        settings.Username,
                        password);

                List<ClientInfo> clients =
                    await routerManager
                        .GetAdGuardClientsAsync();

                _allClients.Clear();

                _allClients.AddRange(
                    clients);

                ApplyFilter();

                StatusMessage =
                    _allClients.Count switch
                    {
                        0 =>
                            "No AdGuard clients were found.",

                        1 =>
                            "1 client loaded.",

                        _ =>
                            $"{_allClients.Count} clients loaded."
                    };
            }
            catch (Exception ex)
            {
                ClearClients();

                StatusMessage =
                    "Unable to load clients: " +
                    ex.Message;
            }
            finally
            {
                IsLoading =
                    false;
            }
        }

        partial void OnSearchTextChanged(
            string value)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string search =
                SearchText.Trim();

            IEnumerable<ClientInfo> filteredClients =
                _allClients;

            if (!string.IsNullOrWhiteSpace(
                    search))
            {
                filteredClients =
                    _allClients.Where(
                        client =>
                            ContainsText(
                                client.Name,
                                search) ||
                            ContainsText(
                                client.IpAddress,
                                search) ||
                            ContainsText(
                                client.MacAddress,
                                search));
            }

            Clients.Clear();

            foreach (ClientInfo client
                     in filteredClients)
            {
                Clients.Add(
                    client);
            }

            if (!IsLoading &&
                _allClients.Count > 0)
            {
                StatusMessage =
                    string.IsNullOrWhiteSpace(
                        search)
                        ? _allClients.Count == 1
                            ? "1 client loaded."
                            : $"{_allClients.Count} clients loaded."
                        : $"{Clients.Count} of " +
                          $"{_allClients.Count} clients shown.";
            }
        }

        private void ClearClients()
        {
            _allClients.Clear();
            Clients.Clear();
            SelectedClient = null;
        }

        private static bool ContainsText(
            string? value,
            string search)
        {
            return !string.IsNullOrWhiteSpace(
                       value) &&
                   value.Contains(
                       search,
                       StringComparison.OrdinalIgnoreCase);
        }
    }
}