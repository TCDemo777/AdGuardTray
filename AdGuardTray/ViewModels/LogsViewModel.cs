using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using AdGuardTray.Models;
using AdGuardTray.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AdGuardTray.ViewModels
{
    public partial class LogsViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly DispatcherTimer _refreshTimer;
        private readonly List<QueryLogEntry> _allEntries = new();
        private RouterManager? _routerManager;

        public ObservableCollection<QueryLogEntry> Entries { get; } = new();

        public IReadOnlyList<int> RefreshIntervals { get; } =
            new[] { 2, 3, 5, 10, 30 };

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private string statusMessage = "No query-log data loaded.";

        [ObservableProperty]
        private string lastUpdatedText = "Not updated yet";

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private bool isPaused;

        [ObservableProperty]
        private bool showBlocked = true;

        [ObservableProperty]
        private bool showAllowed = true;

        [ObservableProperty]
        private int selectedRefreshInterval = 3;

        [ObservableProperty]
        private QueryLogEntry? selectedEntry;

        public string PauseButtonText => IsPaused ? "Resume" : "Pause";

        public LogsViewModel()
        {
            _settingsService = new SettingsService();

            _refreshTimer = new DispatcherTimer(
                DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(SelectedRefreshInterval)
            };

            _refreshTimer.Tick += RefreshTimer_Tick;
        }

        public async Task StartAsync()
        {
            await EnsureRouterManagerAsync();

            if (_routerManager is null)
            {
                return;
            }

            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }

            await LoadLogsAsync();
        }

        public void Stop()
        {
            _refreshTimer.Stop();
        }

        private Task EnsureRouterManagerAsync()
        {
            if (_routerManager is not null)
            {
                return Task.CompletedTask;
            }

            var settings = _settingsService.Load();

            if (string.IsNullOrWhiteSpace(settings.RouterIp) ||
                string.IsNullOrWhiteSpace(settings.Username))
            {
                StatusMessage = "Router settings are incomplete.";
                return Task.CompletedTask;
            }

            string password =
                _settingsService.DecryptPassword(
                    settings.EncryptedPassword);

            if (string.IsNullOrWhiteSpace(password))
            {
                StatusMessage = "The router password is missing.";
                return Task.CompletedTask;
            }

            _routerManager = new RouterManager(
                settings.RouterIp,
                settings.Username,
                password);

            return Task.CompletedTask;
        }

        [RelayCommand]
        public async Task LoadLogsAsync()
        {
            if (IsLoading || IsPaused)
            {
                return;
            }

            await EnsureRouterManagerAsync();

            if (_routerManager is null)
            {
                return;
            }

            IsLoading = true;

            try
            {
                List<QueryLogEntry> entries =
                    await _routerManager.GetQueryLogAsync();

                ApplyEntries(entries);

                LastUpdatedText =
                    $"Updated {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                StatusMessage =
                    "Unable to load query log: " + ex.Message;
                LastUpdatedText =
                    $"Update failed {DateTime.Now:HH:mm:ss}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void ApplyEntries(IEnumerable<QueryLogEntry> entries)
        {
            if (IsPaused)
            {
                return;
            }

            _allEntries.Clear();
            _allEntries.AddRange(entries);

            ApplyFilter();

            StatusMessage = _allEntries.Count switch
            {
                0 => "No query-log entries found.",
                1 => "1 query-log entry loaded.",
                _ => $"{_allEntries.Count} query-log entries loaded."
            };
        }

        [RelayCommand]
        private async Task TogglePauseAsync()
        {
            IsPaused = !IsPaused;
            OnPropertyChanged(nameof(PauseButtonText));

            if (IsPaused)
            {
                StatusMessage = "Live updates paused.";
                return;
            }

            StatusMessage = "Live updates resumed.";
            await LoadLogsAsync();
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();
        partial void OnShowBlockedChanged(bool value) => ApplyFilter();
        partial void OnShowAllowedChanged(bool value) => ApplyFilter();

        partial void OnSelectedRefreshIntervalChanged(int value)
        {
            _refreshTimer.Interval =
                TimeSpan.FromSeconds(Math.Max(1, value));
        }

        private async void RefreshTimer_Tick(
            object? sender,
            EventArgs e)
        {
            await LoadLogsAsync();
        }

        private void ApplyFilter()
        {
            string search = SearchText.Trim();

            IEnumerable<QueryLogEntry> filtered = _allEntries;

            filtered = filtered.Where(entry =>
                (entry.IsBlocked && ShowBlocked) ||
                (!entry.IsBlocked && ShowAllowed));

            if (!string.IsNullOrWhiteSpace(search))
            {
                filtered = filtered.Where(entry =>
                    ContainsText(entry.Client, search) ||
                    ContainsText(entry.Domain, search) ||
                    ContainsText(entry.Status, search));
            }

            Entries.Clear();

            foreach (QueryLogEntry entry in filtered)
            {
                Entries.Add(entry);
            }

            if (!IsLoading && _allEntries.Count > 0)
            {
                StatusMessage =
                    $"{Entries.Count} of {_allEntries.Count} entries shown.";
            }
        }

        [RelayCommand]
        private void ExportCsv()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export query log",
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"AdGuardTray-QueryLog-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("Time,Client,Domain,Status");

            foreach (QueryLogEntry entry in Entries)
            {
                builder.AppendLine(
                    $"{Csv(entry.Time)},{Csv(entry.Client)}," +
                    $"{Csv(entry.Domain)},{Csv(entry.Status)}");
            }

            File.WriteAllText(
                dialog.FileName,
                builder.ToString(),
                Encoding.UTF8);

            StatusMessage =
                $"{Entries.Count} entries exported to CSV.";
        }

        [RelayCommand]
        private void ExportJson()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export query log",
                Filter = "JSON files (*.json)|*.json",
                FileName = $"AdGuardTray-QueryLog-{DateTime.Now:yyyyMMdd-HHmmss}.json"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string json = JsonSerializer.Serialize(
                Entries,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            File.WriteAllText(
                dialog.FileName,
                json,
                Encoding.UTF8);

            StatusMessage =
                $"{Entries.Count} entries exported to JSON.";
        }

        private static string Csv(string? value)
        {
            string safe = value ?? string.Empty;
            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }

        private static bool ContainsText(
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
