using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AdGuardTray.Models;
using AdGuardTray.Services;
using AdGuardTray.ViewModels;

namespace AdGuardTray.Views
{
    public partial class ProtectionView : UserControl
    {
        private readonly SettingsService _settingsService;
        private readonly DispatcherTimer _protectionTimer;

        private RouterManager? _routerManager;
        private string _routerSignature = "";
        private bool _isRefreshing;

        public ProtectionView()
        {
            InitializeComponent();

            _settingsService =
                new SettingsService();

            _protectionTimer =
                new DispatcherTimer
                {
                    Interval =
                        TimeSpan.FromSeconds(3)
                };

            _protectionTimer.Tick +=
                ProtectionTimer_Tick;

            Loaded +=
                ProtectionView_Loaded;

            Unloaded +=
                ProtectionView_Unloaded;
        }

        private async void ProtectionView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            _protectionTimer.Start();

            await RefreshProtectionAsync();
        }

        private void ProtectionView_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            _protectionTimer.Stop();
        }

        private async void ProtectionTimer_Tick(
            object? sender,
            EventArgs e)
        {
            await RefreshProtectionAsync();
        }

        private async void EnableProtection_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RunProtectionActionAsync(
                "Enabling protection...",
                "Protection enabled.",
                router =>
                    router.EnableProtectionAsync());
        }

        private async void DisableProtection_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBoxResult result =
                MessageBox.Show(
                    "Disable AdGuard Home protection until it is manually enabled again?",
                    "Disable Protection",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

            if (result !=
                MessageBoxResult.Yes)
            {
                return;
            }

            await RunProtectionActionAsync(
                "Disabling protection...",
                "Protection disabled.",
                router =>
                    router.DisableProtectionAsync());
        }

        private async void PauseProtection_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Button button ||
                !int.TryParse(
                    button.Tag?.ToString(),
                    out int minutes))
            {
                return;
            }

            TimeSpan duration =
                TimeSpan.FromMinutes(
                    minutes);

            await RunProtectionActionAsync(
                $"Pausing protection for {FormatDuration(duration)}...",
                $"Protection paused for {FormatDuration(duration)}.",
                router =>
                    router.PauseProtectionAsync(
                        duration));
        }

        private async void PauseUntilTomorrow_Click(
            object sender,
            RoutedEventArgs e)
        {
            DateTime tomorrow =
                DateTime.Today.AddDays(1);

            TimeSpan duration =
                tomorrow -
                DateTime.Now;

            if (duration <=
                TimeSpan.Zero)
            {
                duration =
                    TimeSpan.FromHours(24);
            }

            await RunProtectionActionAsync(
                "Pausing protection until tomorrow...",
                "Protection paused until tomorrow.",
                router =>
                    router.PauseProtectionAsync(
                        duration));
        }

        private async void ResumeProtection_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RunProtectionActionAsync(
                "Resuming protection...",
                "Protection resumed.",
                router =>
                    router.ResumeProtectionAsync());
        }

        private async void RefreshProtection_Click(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshProtectionAsync(
                true);
        }

        private async Task RunProtectionActionAsync(
            string busyMessage,
            string successMessage,
            Func<RouterManager,
                Task<AdGuardProtectionStatus>> action)
        {
            DashboardViewModel? viewModel =
                DataContext as DashboardViewModel;

            if (viewModel is null)
            {
                return;
            }

            if (_isRefreshing)
            {
                return;
            }

            _isRefreshing =
                true;

            viewModel.ProtectionControlsEnabled =
                false;

            viewModel.AdGuardProtectionMessage =
                busyMessage;

            try
            {
                RouterManager router =
                    GetRouterManager();

                AdGuardProtectionStatus status =
                    await action(
                        router);

                ApplyProtectionStatus(
                    viewModel,
                    status);

                viewModel.AdGuardProtectionMessage =
                    successMessage;
            }
            catch (Exception ex)
            {
                viewModel.AdGuardProtectionMessage =
                    "Protection command failed: " +
                    ex.Message;
            }
            finally
            {
                viewModel.ProtectionControlsEnabled =
                    true;

                _isRefreshing =
                    false;
            }
        }

        private async Task RefreshProtectionAsync(
            bool showResult = false)
        {
            DashboardViewModel? viewModel =
                DataContext as DashboardViewModel;

            if (viewModel is null ||
                _isRefreshing)
            {
                return;
            }

            _isRefreshing =
                true;

            try
            {
                RouterManager router =
                    GetRouterManager();

                AdGuardProtectionStatus status =
                    await router
                        .GetAdGuardProtectionStatusAsync();

                ApplyProtectionStatus(
                    viewModel,
                    status);

                if (showResult)
                {
                    viewModel.AdGuardProtectionMessage =
                        "Protection status refreshed.";
                }
            }
            catch (Exception ex)
            {
                viewModel.AdGuardProtectionDetail =
                    "Protection status unavailable.";

                viewModel.AdGuardProtectionRemaining =
                    "";

                viewModel.AdGuardProtectionMessage =
                    "Unable to refresh protection: " +
                    ex.Message;
            }
            finally
            {
                _isRefreshing =
                    false;
            }
        }

        private RouterManager GetRouterManager()
        {
            AppSettings settings =
                _settingsService.Load();

            string password =
                _settingsService.DecryptPassword(
                    settings.EncryptedPassword);

            string signature =
                settings.RouterIp +
                "\n" +
                settings.Username +
                "\n" +
                settings.EncryptedPassword;

            if (_routerManager is null ||
                !string.Equals(
                    _routerSignature,
                    signature,
                    StringComparison.Ordinal))
            {
                _routerManager =
                    new RouterManager(
                        settings.RouterIp,
                        settings.Username,
                        password);

                _routerSignature =
                    signature;
            }

            return _routerManager;
        }

        private static void ApplyProtectionStatus(
            DashboardViewModel viewModel,
            AdGuardProtectionStatus status)
        {
            viewModel.AdGuardProtectionEnabled =
                status.IsEnabled;

            if (status.IsEnabled)
            {
                viewModel.AdGuardProtectionDetail =
                    "DNS filtering and protection are active.";

                viewModel.AdGuardProtectionRemaining =
                    "";
            }
            else if (status.IsPaused)
            {
                viewModel.AdGuardProtectionDetail =
                    "Protection is temporarily paused.";

                viewModel.AdGuardProtectionRemaining =
                    "Remaining: " +
                    FormatRemaining(
                        status.RemainingPause);
            }
            else
            {
                viewModel.AdGuardProtectionDetail =
                    "Protection is disabled until manually enabled.";

                viewModel.AdGuardProtectionRemaining =
                    "";
            }

            viewModel.RefreshStatusIndicators();
        }

        private static string FormatRemaining(
            TimeSpan duration)
        {
            if (duration <=
                TimeSpan.Zero)
            {
                return "less than a minute";
            }

            if (duration.TotalDays >= 1)
            {
                return
                    $"{(int)duration.TotalDays}d " +
                    $"{duration.Hours}h " +
                    $"{duration.Minutes}m";
            }

            if (duration.TotalHours >= 1)
            {
                return
                    $"{(int)duration.TotalHours}h " +
                    $"{duration.Minutes}m";
            }

            return
                $"{Math.Max(1, duration.Minutes)}m";
        }

        private static string FormatDuration(
            TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
            {
                double hours =
                    duration.TotalHours;

                return hours == 1
                    ? "1 hour"
                    : $"{hours:0.#} hours";
            }

            return
                $"{duration.TotalMinutes:0} minutes";
        }
    }
}
