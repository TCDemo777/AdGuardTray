using System;
using System.Diagnostics;
using System.Windows;
using AdGuardTray.Models;
using AdGuardTray.Services;
using AdGuardTray.Services.Insights;
using AdGuardTray.Tray;
using AdGuardTray.Views;
using AdGuardTray.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AdGuardTray
{
    public partial class App : Application
    {
        private DashboardWindow? _dashboardWindow;
        private TrayManager? _trayManager;
        private bool _trayNoticeShown;
        private ServiceProvider? _services;

        public IServiceProvider Services => _services
            ?? throw new InvalidOperationException("Application services are not available.");

        public bool IsExitRequested { get; private set; }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<SettingsService>();
            serviceCollection.AddSingleton<DatabaseInitializer>();
            serviceCollection.AddSingleton<SQLiteDataStore>();
            serviceCollection.AddSingleton<IDataStore>(provider =>
                provider.GetRequiredService<SQLiteDataStore>());
            serviceCollection.AddSingleton<HistoryRepository>();
            serviceCollection.AddSingleton<WanHistoryCollector>();
            serviceCollection.AddSingleton<IRouterManagerProvider,
                RouterManagerProvider>();
            serviceCollection.AddSingleton(
                _ => new NotificationService(Dispatcher));
            serviceCollection.AddSingleton<AdGuardProtectionNotificationTracker>();
            serviceCollection.AddSingleton<DeviceHistoryService>();
            serviceCollection.AddSingleton<NewDeviceNotificationTracker>();
            serviceCollection.AddSingleton<IInsightRule, RouterUptimeRule>();
            serviceCollection.AddSingleton<IInsightRule, InternetDisconnectRule>();
            serviceCollection.AddSingleton<IInsightRule, AdGuardDisabledRule>();
            serviceCollection.AddSingleton<IInsightRule, NewDevicesTodayRule>();
            serviceCollection.AddSingleton<IInsightRule, NoClientsConnectedRule>();
            serviceCollection.AddSingleton<IInsightRule, HighCpuRule>();
            serviceCollection.AddSingleton<IInsightRule, HighMemoryRule>();
            serviceCollection.AddSingleton<InsightEngine>();
            serviceCollection.AddSingleton<NotificationCentreViewModel>();
            serviceCollection.AddTransient<ClientsViewModel>();
            serviceCollection.AddTransient<LogsViewModel>();
            serviceCollection.AddSingleton<ProtectionViewModel>();
            serviceCollection.AddTransient<GlobalSearchViewModel>();
            serviceCollection.AddTransient<SettingsViewModel>();
            _services = serviceCollection.BuildServiceProvider();

            await Services.GetRequiredService<IDataStore>()
                .InitializeAsync();
            int historySchemaVersion = await Services
                .GetRequiredService<HistoryRepository>()
                .GetSchemaVersionAsync();
            if (historySchemaVersion != DatabaseInitializer.CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Unexpected history database schema version {historySchemaVersion}.");
            }

            await Services.GetRequiredService<NotificationService>()
                .InitializeAsync();
            await Services.GetRequiredService<DeviceHistoryService>()
                .InitializeAsync();

            AppSettings savedSettings = new SettingsService().Load();
            ThemeService.Initialize(savedSettings.Theme);

            if (!HasUsableSavedSettings())
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Window settingsWindow = CreateFirstRunSettingsWindow();
                MainWindow = settingsWindow;
                settingsWindow.Show();
                return;
            }

            StartMainApplication();
        }

        public void CompleteFirstRun(Window settingsWindow)
        {
            if (!HasUsableSavedSettings())
            {
                MessageBox.Show(
                    "The router settings are incomplete or the saved password could not be read.",
                    "RouterPilot Setup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            StartMainApplication();
            settingsWindow.Close();
        }

        private void StartMainApplication()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (_dashboardWindow is null)
            {
                _dashboardWindow = new DashboardWindow();
                _dashboardWindow.Closed += (_, _) =>
                {
                    if (IsExitRequested)
                        _dashboardWindow = null;
                };
            }

            _trayManager ??= new TrayManager(
                ShowDashboard,
                RefreshDashboard,
                ExitApplication);

            MainWindow = _dashboardWindow;
            ShowDashboard();
        }

        public void HideDashboard()
        {
            if (_dashboardWindow is null || IsExitRequested)
                return;

            _dashboardWindow.Hide();

            if (!_trayNoticeShown)
            {
                _trayManager?.ShowStillRunningMessage();
                _trayNoticeShown = true;
            }
        }

        public void ShowDashboard()
        {
            if (_dashboardWindow is null)
                return;

            if (!_dashboardWindow.IsVisible)
                _dashboardWindow.Show();

            if (_dashboardWindow.WindowState == WindowState.Minimized)
                _dashboardWindow.WindowState = WindowState.Normal;

            _dashboardWindow.Activate();
            _dashboardWindow.Topmost = true;
            _dashboardWindow.Topmost = false;
            _dashboardWindow.Focus();
        }

        private async void RefreshDashboard()
        {
            ShowDashboard();
            if (_dashboardWindow is not null)
                await _dashboardWindow.RefreshNowAsync();
        }

        private async void ExitApplication()
        {
            IsExitRequested = true;
            _trayManager?.Dispose();
            _trayManager = null;

            if (_dashboardWindow is not null)
            {
                await _dashboardWindow.PrepareForShutdownAsync();
                _dashboardWindow.Close();
            }

            _dashboardWindow = null;

            if (_services is not null)
            {
                try
                {
                    await _services
                        .GetRequiredService<WanHistoryCollector>()
                        .DisposeAsync();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"Unable to flush WAN history during shutdown: {ex}");
                }

                try
                {
                    await _services
                        .GetRequiredService<DeviceHistoryService>()
                        .DisposeAsync();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"Unable to flush device history during shutdown: {ex}");
                }

                try
                {
                    await _services
                        .GetRequiredService<NotificationService>()
                        .DisposeAsync();
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"Unable to flush notification history during shutdown: {ex}");
                }

                try
                {
                    await _services.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"Unable to dispose application services cleanly: {ex}");
                }

                _services = null;
            }

            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayManager?.Dispose();
            base.OnExit(e);
        }

        private static bool HasUsableSavedSettings()
        {
            try
            {
                var settingsService = new SettingsService();
                AppSettings settings = settingsService.Load();

                if (string.IsNullOrWhiteSpace(settings.RouterHost) ||
                    string.IsNullOrWhiteSpace(settings.Username))
                    return false;

                if (!settings.RememberPassword ||
                    string.IsNullOrWhiteSpace(settings.EncryptedPassword))
                    return false;

                string password = settingsService.DecryptPassword(
                    settings.EncryptedPassword);

                return !string.IsNullOrWhiteSpace(password);
            }
            catch
            {
                return false;
            }
        }

        private static Window CreateFirstRunSettingsWindow()
        {
            return new Window
            {
                Title = "RouterPilot Setup",
                Width = 920,
                Height = 700,
                MinWidth = 760,
                MinHeight = 560,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new SettingsView()
            };
        }
    }
}
