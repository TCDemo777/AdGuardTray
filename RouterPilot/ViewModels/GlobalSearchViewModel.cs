using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using RouterPilot.Models;
using RouterPilot.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RouterPilot.ViewModels
{
    public partial class GlobalSearchViewModel : ObservableObject
    {
        private readonly IRouterManagerProvider _routerManagerProvider;
        private readonly List<ClientInfo> _clients = new();
        private readonly List<QueryLogEntry> _logs = new();

        public ObservableCollection<GlobalSearchResult> Results { get; } = new();

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private string statusMessage =
            "Enter at least two characters to search clients and recent DNS activity.";

        [ObservableProperty]
        private bool isLoading;

        public GlobalSearchViewModel(
            IRouterManagerProvider routerManagerProvider)
        {
            _routerManagerProvider = routerManagerProvider;
        }

        [RelayCommand]
        public async Task RefreshIndexAsync()
        {
            if (IsLoading)
            {
                return;
            }

            IsLoading = true;
            StatusMessage = "Refreshing search index...";

            try
            {
                RouterManager routerManager =
                    await _routerManagerProvider.GetRouterManagerAsync();
                List<ClientInfo> clients =
                    await routerManager.GetAdGuardClientsAsync();

                List<QueryLogEntry> logs =
                    await routerManager.GetQueryLogAsync();

                _clients.Clear();
                _clients.AddRange(clients);

                _logs.Clear();
                _logs.AddRange(logs);

                ApplySearch();

                StatusMessage =
                    $"Search index updated: {_clients.Count} clients and " +
                    $"{_logs.Count} recent queries.";
            }
            catch (Exception ex)
            {
                StatusMessage =
                    "Unable to refresh global search: " +
                    ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        partial void OnSearchTextChanged(string value)
        {
            ApplySearch();
        }

        private void ApplySearch()
        {
            Results.Clear();

            string search =
                SearchText.Trim();

            if (search.Length < 2)
            {
                StatusMessage =
                    "Enter at least two characters to search clients and recent DNS activity.";
                return;
            }

            IEnumerable<GlobalSearchResult> clients =
                _clients
                    .Where(client =>
                        Contains(client.Name, search) ||
                        Contains(client.IpAddress, search) ||
                        Contains(client.MacAddress, search))
                    .Take(25)
                    .Select(client =>
                        new GlobalSearchResult
                        {
                            Category = "Client",
                            Title = client.Name,
                            Subtitle =
                                $"{client.IpAddress} · {client.MacAddress}",
                            Detail =
                                $"{client.TotalQueries:N0} queries · " +
                                $"{client.BlockedQueries:N0} blocked · " +
                                $"{client.BlockRate:F1}% block rate",
                            BadgeText = "Client",
                            BadgeColour = "#3367D6"
                        });

            IEnumerable<GlobalSearchResult> domains =
                _logs
                    .Where(entry =>
                        Contains(entry.Domain, search) ||
                        Contains(entry.Client, search))
                    .GroupBy(
                        entry => entry.Domain,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group =>
                    {
                        int total = group.Count();
                        int blocked =
                            group.Count(entry => entry.IsBlocked);

                        return new GlobalSearchResult
                        {
                            Category = "Domain",
                            Title = group.Key,
                            Subtitle =
                                $"{total:N0} recent queries · " +
                                $"{blocked:N0} blocked",
                            Detail =
                                string.Join(
                                    ", ",
                                    group
                                        .Select(entry => entry.Client)
                                        .Where(value =>
                                            !string.IsNullOrWhiteSpace(value))
                                        .Distinct(
                                            StringComparer.OrdinalIgnoreCase)
                                        .Take(5)),
                            BadgeText =
                                blocked == total && total > 0
                                    ? "Blocked"
                                    : blocked == 0
                                        ? "Allowed"
                                        : "Mixed",
                            BadgeColour =
                                blocked == total && total > 0
                                    ? "#C62828"
                                    : blocked == 0
                                        ? "#16803C"
                                        : "#B26A00"
                        };
                    })
                    .Take(50);

            foreach (GlobalSearchResult result in
                     clients.Concat(domains)
                            .OrderBy(result => result.Category)
                            .ThenBy(result => result.Title))
            {
                Results.Add(result);
            }

            StatusMessage =
                Results.Count == 0
                    ? "No matching clients or domains found."
                    : $"{Results.Count} results found.";
        }

        private static bool Contains(
            string? value,
            string search)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Contains(
                       search,
                       StringComparison.OrdinalIgnoreCase);
        }
    }
}
