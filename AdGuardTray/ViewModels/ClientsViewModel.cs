using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AdGuardTray.Models;

namespace AdGuardTray.ViewModels
{
    public partial class ClientsViewModel : ObservableObject
    {
        public ObservableCollection<ClientInfo> Clients { get; } =
            new();

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private ClientInfo? selectedClient;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private string statusMessage = "No client data loaded.";
    }
}