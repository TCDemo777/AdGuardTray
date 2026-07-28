using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AdGuardTray.Models;
using AdGuardTray.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdGuardTray.ViewModels
{
    public sealed class ProtectionViewModel : ObservableObject, IDisposable
    {
        private readonly SettingsService _settingsService = new();
        private readonly DispatcherTimer _timer;
        private RouterManager? _routerManager;
        private string _routerSignature = "";
        private bool _isBusy;
        private bool _isInitialising;
        private string _statusText = "Loading...";
        private string _statusDetail = "Reading AdGuard Home settings.";
        private string _remaining = "";
        private string _message = "";
        private string _blockedServicesStatus = "Loading available services...";
        private string _blockedServicesSearch = "";
        private bool _showBlockedOnly;
        private string _selectedBlockedServiceCategory = "All categories";
        private string _profileName = "Custom";
        private bool _filteringEnabled;
        private bool _safeBrowsingEnabled;
        private bool _safeSearchEnabled;
        private bool _parentalEnabled;
        private bool _queryLogEnabled;
        private string _newRuleDomain = "";
        private string _newRewriteDomain = "";
        private string _newRewriteAnswer = "";
        private CustomFilteringRule? _selectedRule;
        private DnsRewriteRule? _selectedRewrite;
        private AdGuardProtectionOptions _options = new();
        private AdGuardBlockedServicesConfig _blockedConfig = new();

        public ProtectionViewModel()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _timer.Tick += async (_, _) => await RefreshProtectionStatusAsync(false);

            RefreshAllCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsBusy);
            EnableProtectionCommand = new AsyncRelayCommand(() => RunStatusActionAsync("Enabling protection...", "Protection enabled.", r => r.EnableProtectionAsync()), () => !IsBusy);
            DisableProtectionCommand = new AsyncRelayCommand(DisableProtectionAsync, () => !IsBusy);
            ResumeProtectionCommand = new AsyncRelayCommand(() => RunStatusActionAsync("Resuming protection...", "Protection resumed.", r => r.ResumeProtectionAsync()), () => !IsBusy);
            Pause30Command = new AsyncRelayCommand(() => PauseAsync(TimeSpan.FromMinutes(30)), () => !IsBusy);
            Pause1HourCommand = new AsyncRelayCommand(() => PauseAsync(TimeSpan.FromHours(1)), () => !IsBusy);
            Pause4HoursCommand = new AsyncRelayCommand(() => PauseAsync(TimeSpan.FromHours(4)), () => !IsBusy);
            PauseUntilTomorrowCommand = new AsyncRelayCommand(PauseUntilTomorrowAsync, () => !IsBusy);
            ApplyStandardProfileCommand = new AsyncRelayCommand(() => ApplyProfileAsync("Standard", true, true, false, false, true), () => !IsBusy);
            ApplyFamilyProfileCommand = new AsyncRelayCommand(() => ApplyProfileAsync("Family", true, true, true, true, true), () => !IsBusy);
            ApplyPrivacyProfileCommand = new AsyncRelayCommand(() => ApplyProfileAsync("Privacy", true, true, false, true, false), () => !IsBusy);
            SaveBlockedServicesCommand = new AsyncRelayCommand(SaveBlockedServicesAsync, () => !IsBusy);
            SelectAllServicesCommand = new RelayCommand(() => SetAllBlockedServices(true), () => !IsBusy);
            ClearAllServicesCommand = new RelayCommand(() => SetAllBlockedServices(false), () => !IsBusy);
            BlockedServiceCategories.Add("All categories");
            BlockedServicesView = CollectionViewSource.GetDefaultView(BlockedServices);
            BlockedServicesView.Filter = FilterBlockedService;
            BlockedServicesView.SortDescriptions.Add(
                new SortDescription(
                    nameof(BlockedServiceItem.Name),
                    ListSortDirection.Ascending));
            AddDenyRuleCommand = new AsyncRelayCommand(() => AddRuleAsync(false), () => !IsBusy);
            AddAllowRuleCommand = new AsyncRelayCommand(() => AddRuleAsync(true), () => !IsBusy);
            DeleteRuleCommand = new AsyncRelayCommand(DeleteRuleAsync, () => !IsBusy && SelectedRule is not null);
            AddRewriteCommand = new AsyncRelayCommand(AddRewriteAsync, () => !IsBusy);
            DeleteRewriteCommand = new AsyncRelayCommand(DeleteRewriteAsync, () => !IsBusy && SelectedRewrite is not null);
        }

        public ObservableCollection<BlockedServiceItem> BlockedServices { get; } = new();
        public ObservableCollection<string> BlockedServiceCategories { get; } = new();
        public ICollectionView BlockedServicesView { get; }
        public ObservableCollection<CustomFilteringRule> FilteringRules { get; } = new();
        public ObservableCollection<DnsRewriteRule> DnsRewrites { get; } = new();

        public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(ControlsEnabled)); NotifyCommands(); } } }
        public bool ControlsEnabled => !IsBusy;
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
        public string StatusDetail { get => _statusDetail; private set => SetProperty(ref _statusDetail, value); }
        public string Remaining { get => _remaining; private set => SetProperty(ref _remaining, value); }
        public string Message { get => _message; private set => SetProperty(ref _message, value); }
        public string BlockedServicesStatus { get => _blockedServicesStatus; private set => SetProperty(ref _blockedServicesStatus, value); }
        public string BlockedServicesSearch { get => _blockedServicesSearch; set { if (SetProperty(ref _blockedServicesSearch, value)) BlockedServicesView.Refresh(); } }
        public bool ShowBlockedOnly { get => _showBlockedOnly; set { if (SetProperty(ref _showBlockedOnly, value)) BlockedServicesView.Refresh(); } }
        public string SelectedBlockedServiceCategory
        {
            get => _selectedBlockedServiceCategory;
            set
            {
                if (SetProperty(ref _selectedBlockedServiceCategory, value))
                    BlockedServicesView.Refresh();
            }
        }
        public string BlockedServicesSelectionSummary => $"{BlockedServices.Count(s => s.IsBlocked)} selected";
        public string ProfileName { get => _profileName; private set => SetProperty(ref _profileName, value); }

        public bool FilteringEnabled { get => _filteringEnabled; set { if (SetProperty(ref _filteringEnabled, value) && !_isInitialising) _ = UpdateOptionAsync("DNS filtering", r => r.SetFilteringEnabledAsync(value)); } }
        public bool SafeBrowsingEnabled { get => _safeBrowsingEnabled; set { if (SetProperty(ref _safeBrowsingEnabled, value) && !_isInitialising) _ = UpdateOptionAsync("Safe Browsing", r => r.SetSafeBrowsingEnabledAsync(value)); } }
        public bool SafeSearchEnabled { get => _safeSearchEnabled; set { if (SetProperty(ref _safeSearchEnabled, value) && !_isInitialising) _ = UpdateOptionAsync("Safe Search", r => r.SetSafeSearchEnabledAsync(value, _options.SafeSearch)); } }
        public bool ParentalEnabled { get => _parentalEnabled; set { if (SetProperty(ref _parentalEnabled, value) && !_isInitialising) _ = UpdateOptionAsync("Parental Control", r => r.SetParentalEnabledAsync(value)); } }
        public bool QueryLogEnabled { get => _queryLogEnabled; set { if (SetProperty(ref _queryLogEnabled, value) && !_isInitialising) _ = UpdateOptionAsync("Query logging", r => r.SetQueryLogEnabledAsync(value, _options)); } }

        public string NewRuleDomain { get => _newRuleDomain; set => SetProperty(ref _newRuleDomain, value); }
        public string NewRewriteDomain { get => _newRewriteDomain; set => SetProperty(ref _newRewriteDomain, value); }
        public string NewRewriteAnswer { get => _newRewriteAnswer; set => SetProperty(ref _newRewriteAnswer, value); }
        public CustomFilteringRule? SelectedRule { get => _selectedRule; set { if (SetProperty(ref _selectedRule, value)) NotifyCommands(); } }
        public DnsRewriteRule? SelectedRewrite { get => _selectedRewrite; set { if (SetProperty(ref _selectedRewrite, value)) NotifyCommands(); } }

        public IAsyncRelayCommand RefreshAllCommand { get; }
        public IAsyncRelayCommand EnableProtectionCommand { get; }
        public IAsyncRelayCommand DisableProtectionCommand { get; }
        public IAsyncRelayCommand ResumeProtectionCommand { get; }
        public IAsyncRelayCommand Pause30Command { get; }
        public IAsyncRelayCommand Pause1HourCommand { get; }
        public IAsyncRelayCommand Pause4HoursCommand { get; }
        public IAsyncRelayCommand PauseUntilTomorrowCommand { get; }
        public IAsyncRelayCommand ApplyStandardProfileCommand { get; }
        public IAsyncRelayCommand ApplyFamilyProfileCommand { get; }
        public IAsyncRelayCommand ApplyPrivacyProfileCommand { get; }
        public IAsyncRelayCommand SaveBlockedServicesCommand { get; }
        public IRelayCommand SelectAllServicesCommand { get; }
        public IRelayCommand ClearAllServicesCommand { get; }
        public IAsyncRelayCommand AddDenyRuleCommand { get; }
        public IAsyncRelayCommand AddAllowRuleCommand { get; }
        public IAsyncRelayCommand DeleteRuleCommand { get; }
        public IAsyncRelayCommand AddRewriteCommand { get; }
        public IAsyncRelayCommand DeleteRewriteCommand { get; }

        public async Task StartAsync()
        {
            _timer.Start();
            await RefreshAllAsync();
        }

        public void Stop() => _timer.Stop();
        public void Dispose() { _timer.Stop(); _timer.Tick -= async (_, _) => await RefreshProtectionStatusAsync(false); }

        private async Task RefreshAllAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            Message = "Refreshing all protection settings...";
            try
            {
                RouterManager router = GetRouterManager();
                AdGuardProtectionStatus status = await router.GetAdGuardProtectionStatusAsync();
                _options = await router.GetProtectionOptionsAsync();
                (var services, _blockedConfig) = await router.GetBlockedServicesAsync();
                var rules = await router.GetCustomFilteringRulesAsync();
                var rewrites = await router.GetDnsRewritesAsync();

                ApplyStatus(status);
                _isInitialising = true;
                FilteringEnabled = _options.FilteringEnabled;
                SafeBrowsingEnabled = _options.SafeBrowsingEnabled;
                SafeSearchEnabled = _options.SafeSearchEnabled;
                ParentalEnabled = _options.ParentalEnabled;
                QueryLogEnabled = _options.QueryLogEnabled;
                _isInitialising = false;
                DetermineProfile();

                foreach (var oldService in BlockedServices) oldService.PropertyChanged -= BlockedService_PropertyChanged;
                BlockedServices.Clear();
                foreach (var service in services.OrderBy(s => s.Name))
                {
                    service.PropertyChanged += BlockedService_PropertyChanged;
                    BlockedServices.Add(service);
                }

                BlockedServiceCategories.Clear();
                BlockedServiceCategories.Add("All categories");
                foreach (string category in BlockedServices
                    .Select(service => service.Category)
                    .Where(category => !string.IsNullOrWhiteSpace(category))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(category => category))
                {
                    BlockedServiceCategories.Add(category);
                }

                if (!BlockedServiceCategories.Contains(SelectedBlockedServiceCategory))
                    SelectedBlockedServiceCategory = "All categories";

                BlockedServicesView.Refresh();
                OnPropertyChanged(nameof(BlockedServicesSelectionSummary));
                BlockedServicesStatus = BlockedServices.Count == 0
                    ? "No blocked-service catalogue was returned by this AdGuard Home build."
                    : $"{BlockedServices.Count} services available. Select services and save your changes.";
                FilteringRules.Clear();
                foreach (var rule in rules) FilteringRules.Add(rule);
                DnsRewrites.Clear();
                foreach (var rewrite in rewrites) DnsRewrites.Add(rewrite);
                Message = "Protection settings refreshed.";
            }
            catch (Exception ex)
            {
                if (BlockedServices.Count == 0)
                    BlockedServicesStatus = "Blocked services could not be loaded. Use Refresh all to try again.";
                Message = "Unable to refresh protection settings: " + ex.Message;
            }
            finally { _isInitialising = false; IsBusy = false; }
        }

        private async Task RefreshProtectionStatusAsync(bool showMessage)
        {
            if (IsBusy) return;
            try { ApplyStatus(await GetRouterManager().GetAdGuardProtectionStatusAsync()); if (showMessage) Message = "Protection status refreshed."; }
            catch (Exception ex) { StatusDetail = "Protection status unavailable."; if (showMessage) Message = ex.Message; }
        }

        private async Task DisableProtectionAsync()
        {
            if (MessageBox.Show("Disable protection until it is manually enabled again?", "Disable Protection", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await RunStatusActionAsync("Disabling protection...", "Protection disabled.", r => r.DisableProtectionAsync());
        }

        private Task PauseAsync(TimeSpan duration) => RunStatusActionAsync($"Pausing protection for {FormatDuration(duration)}...", $"Protection paused for {FormatDuration(duration)}.", r => r.PauseProtectionAsync(duration));
        private Task PauseUntilTomorrowAsync()
        {
            TimeSpan duration = DateTime.Today.AddDays(1) - DateTime.Now;
            if (duration <= TimeSpan.Zero) duration = TimeSpan.FromHours(24);
            return RunStatusActionAsync("Pausing protection until tomorrow...", "Protection paused until tomorrow.", r => r.PauseProtectionAsync(duration));
        }

        private async Task RunStatusActionAsync(string busy, string success, Func<RouterManager, Task<AdGuardProtectionStatus>> action)
        {
            if (IsBusy) return;
            IsBusy = true;
            Message = busy;

            try
            {
                AdGuardProtectionStatus status =
                    await action(GetRouterManager());

                ApplyStatus(status);

                // Update the Overview page immediately instead of waiting for
                // its normal router polling interval.
                ProtectionStateNotifier.Publish(status);

                Message = success;
            }
            catch (Exception ex)
            {
                Message = "Protection command failed: " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task UpdateOptionAsync(string label, Func<RouterManager, Task> action)
        {
            if (IsBusy) return;
            IsBusy = true; Message = $"Updating {label}...";
            try { await action(GetRouterManager()); Message = $"{label} updated."; _options = await GetRouterManager().GetProtectionOptionsAsync(); DetermineProfile(); }
            catch (Exception ex) { Message = $"Unable to update {label}: {ex.Message}"; await RefreshOptionsOnlyAsync(); }
            finally { IsBusy = false; }
        }

        private async Task RefreshOptionsOnlyAsync()
        {
            try
            {
                _options = await GetRouterManager().GetProtectionOptionsAsync();
                _isInitialising = true;
                FilteringEnabled = _options.FilteringEnabled; SafeBrowsingEnabled = _options.SafeBrowsingEnabled; SafeSearchEnabled = _options.SafeSearchEnabled; ParentalEnabled = _options.ParentalEnabled; QueryLogEnabled = _options.QueryLogEnabled;
                DetermineProfile();
            }
            finally { _isInitialising = false; }
        }

        private async Task ApplyProfileAsync(string name, bool filtering, bool safeBrowsing, bool parental, bool safeSearch, bool queryLog)
        {
            if (IsBusy) return;
            IsBusy = true; Message = $"Applying {name} profile...";
            try
            {
                RouterManager r = GetRouterManager();
                await r.SetFilteringEnabledAsync(filtering);
                await r.SetSafeBrowsingEnabledAsync(safeBrowsing);
                await r.SetParentalEnabledAsync(parental);
                await r.SetSafeSearchEnabledAsync(safeSearch, _options.SafeSearch);
                await r.SetQueryLogEnabledAsync(queryLog, _options);
                await RefreshOptionsOnlyAsync();
                ProfileName = name; Message = $"{name} profile applied.";
            }
            catch (Exception ex) { Message = "Unable to apply profile: " + ex.Message; }
            finally { IsBusy = false; }
        }

        private bool FilterBlockedService(object item)
        {
            if (item is not BlockedServiceItem service) return false;
            if (ShowBlockedOnly && !service.IsBlocked) return false;

            if (!string.Equals(
                    SelectedBlockedServiceCategory,
                    "All categories",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    service.Category,
                    SelectedBlockedServiceCategory,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(BlockedServicesSearch) ||
                   service.Name.Contains(BlockedServicesSearch.Trim(), StringComparison.OrdinalIgnoreCase) ||
                   service.Id.Contains(BlockedServicesSearch.Trim(), StringComparison.OrdinalIgnoreCase) ||
                   service.Category.Contains(BlockedServicesSearch.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private void BlockedService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BlockedServiceItem.IsBlocked)) return;
            OnPropertyChanged(nameof(BlockedServicesSelectionSummary));
            if (ShowBlockedOnly) BlockedServicesView.Refresh();
        }

        private void SetAllBlockedServices(bool blocked)
        {
            foreach (BlockedServiceItem service in BlockedServicesView.Cast<BlockedServiceItem>())
                service.IsBlocked = blocked;
            OnPropertyChanged(nameof(BlockedServicesSelectionSummary));
            Message = blocked ? "All visible services selected." : "All visible services cleared.";
        }

        private async Task SaveBlockedServicesAsync()
        {
            if (IsBusy) return;
            IsBusy = true; Message = "Saving blocked services...";
            try { await GetRouterManager().UpdateBlockedServicesAsync(BlockedServices.Where(s => s.IsBlocked).Select(s => s.Id), _blockedConfig.ScheduleJson); Message = "Blocked services updated."; }
            catch (Exception ex) { Message = "Unable to update blocked services: " + ex.Message; }
            finally { IsBusy = false; }
        }

        private async Task AddRuleAsync(bool allow)
        {
            string domain = NormaliseDomain(NewRuleDomain);
            if (domain.Length == 0) { Message = "Enter a domain first."; return; }
            string rule = allow ? $"@@||{domain}^" : $"||{domain}^";
            var all = FilteringRules.Select(r => r.Rule).Append(rule).Distinct(StringComparer.Ordinal).ToArray();
            await SaveRulesAsync(all, allow ? "Allow rule added." : "Block rule added.");
            NewRuleDomain = "";
        }

        private async Task DeleteRuleAsync()
        {
            if (SelectedRule is null) return;
            await SaveRulesAsync(FilteringRules.Where(r => !ReferenceEquals(r, SelectedRule)).Select(r => r.Rule).ToArray(), "Rule deleted.");
        }

        private async Task SaveRulesAsync(string[] rules, string success)
        {
            if (IsBusy) return;
            IsBusy = true; Message = "Saving custom filtering rules...";
            try
            {
                await GetRouterManager().SetCustomFilteringRulesAsync(rules);
                FilteringRules.Clear(); foreach (var rule in await GetRouterManager().GetCustomFilteringRulesAsync()) FilteringRules.Add(rule);
                Message = success;
            }
            catch (Exception ex) { Message = "Unable to save filtering rules: " + ex.Message; }
            finally { IsBusy = false; }
        }

        private async Task AddRewriteAsync()
        {
            string domain = NormaliseDomain(NewRewriteDomain); string answer = NewRewriteAnswer.Trim();
            if (domain.Length == 0 || answer.Length == 0) { Message = "Enter both a domain and an answer."; return; }
            if (IsBusy) return;
            IsBusy = true; Message = "Adding DNS rewrite...";
            try { await GetRouterManager().AddDnsRewriteAsync(domain, answer); await ReloadRewritesAsync(); NewRewriteDomain = ""; NewRewriteAnswer = ""; Message = "DNS rewrite added."; }
            catch (Exception ex) { Message = "Unable to add DNS rewrite: " + ex.Message; }
            finally { IsBusy = false; }
        }

        private async Task DeleteRewriteAsync()
        {
            if (SelectedRewrite is null || IsBusy) return;
            IsBusy = true; Message = "Deleting DNS rewrite...";
            try { await GetRouterManager().DeleteDnsRewriteAsync(SelectedRewrite.Domain, SelectedRewrite.Answer); await ReloadRewritesAsync(); Message = "DNS rewrite deleted."; }
            catch (Exception ex) { Message = "Unable to delete DNS rewrite: " + ex.Message; }
            finally { IsBusy = false; }
        }
        private async Task ReloadRewritesAsync() { DnsRewrites.Clear(); foreach (var x in await GetRouterManager().GetDnsRewritesAsync()) DnsRewrites.Add(x); }

        private void ApplyStatus(AdGuardProtectionStatus status)
        {
            if (status.IsEnabled) { StatusText = "Enabled"; StatusDetail = "DNS filtering and protection are active."; Remaining = ""; }
            else if (status.IsPaused) { StatusText = "Paused"; StatusDetail = "Protection is temporarily paused."; Remaining = "Remaining: " + FormatRemaining(status.RemainingPause); }
            else { StatusText = "Disabled"; StatusDetail = "Protection is disabled until manually enabled."; Remaining = ""; }
        }

        private void DetermineProfile()
        {
            ProfileName = FilteringEnabled && SafeBrowsingEnabled && ParentalEnabled && SafeSearchEnabled && QueryLogEnabled ? "Family" :
                          FilteringEnabled && SafeBrowsingEnabled && !ParentalEnabled && SafeSearchEnabled && !QueryLogEnabled ? "Privacy" :
                          FilteringEnabled && SafeBrowsingEnabled && !ParentalEnabled && !SafeSearchEnabled && QueryLogEnabled ? "Standard" : "Custom";
        }

        private RouterManager GetRouterManager()
        {
            AppSettings settings = _settingsService.Load();
            string password = _settingsService.DecryptPassword(settings.EncryptedPassword);
            string signature = settings.RouterIp + "\n" + settings.Username + "\n" + settings.EncryptedPassword;
            if (_routerManager is null || !string.Equals(_routerSignature, signature, StringComparison.Ordinal)) { _routerManager = new RouterManager(settings.RouterIp, settings.Username, password); _routerSignature = signature; }
            return _routerManager;
        }

        private void NotifyCommands()
        {
            foreach (var command in new[] { RefreshAllCommand, EnableProtectionCommand, DisableProtectionCommand, ResumeProtectionCommand, Pause30Command, Pause1HourCommand, Pause4HoursCommand, PauseUntilTomorrowCommand, ApplyStandardProfileCommand, ApplyFamilyProfileCommand, ApplyPrivacyProfileCommand, SaveBlockedServicesCommand, AddDenyRuleCommand, AddAllowRuleCommand, DeleteRuleCommand, AddRewriteCommand, DeleteRewriteCommand }) command.NotifyCanExecuteChanged();
            SelectAllServicesCommand.NotifyCanExecuteChanged();
            ClearAllServicesCommand.NotifyCanExecuteChanged();
        }
        private static string NormaliseDomain(string value) => value.Trim().TrimEnd('.').ToLowerInvariant();
        private static string FormatRemaining(TimeSpan d) => d.TotalDays >= 1 ? $"{(int)d.TotalDays}d {d.Hours}h {d.Minutes}m" : d.TotalHours >= 1 ? $"{(int)d.TotalHours}h {d.Minutes}m" : $"{Math.Max(1, d.Minutes)}m";
        private static string FormatDuration(TimeSpan d) => d.TotalHours >= 1 ? (d.TotalHours == 1 ? "1 hour" : $"{d.TotalHours:0.#} hours") : $"{d.TotalMinutes:0} minutes";
    }
}
