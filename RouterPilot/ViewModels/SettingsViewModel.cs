using System;
using RouterPilot.Configuration;
using RouterPilot.Models;
using RouterPilot.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RouterPilot.ViewModels
{
    public class SettingsViewModel : ObservableObject
    {
        public event EventHandler? SettingsSaved;
        private readonly SettingsService _settingsService;
        private readonly IRouterManagerProvider _routerManagerProvider;
        private readonly IToastNotificationService _toastNotificationService;
        public AdGuardAvailabilityService AdGuardAvailability { get; }

        private string _routerIp = "";
        private string _username = "";
        private string _password = "";
        private bool _rememberPassword;
        private bool _startWithWindows;
        private string _theme = ThemeService.SystemTheme;
        private int _refreshIntervalSeconds = 30;
        private int _defaultPauseMinutes = 30;
        private string _statusMessage = "Settings loaded.";
        private bool _hasUnsavedChanges;
        private bool _isLoading;
        private bool _notificationsEnabled = true;
        private bool _notificationCentreEnabled = true;
        private bool _windowsToastsEnabled = true;
        private bool _quietHoursEnabled;
        private string _quietHoursStart = "22:00";
        private string _quietHoursEnd = "07:00";

        public string RouterIp
        {
            get => _routerIp;
            set
            {
                if (SetProperty(ref _routerIp, value))
                {
                    MarkChanged();
                }
            }
        }

        public string Username
        {
            get => _username;
            set
            {
                if (SetProperty(ref _username, value))
                {
                    MarkChanged();
                }
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (SetProperty(ref _password, value))
                {
                    MarkChanged();
                }
            }
        }

        public bool RememberPassword
        {
            get => _rememberPassword;
            set
            {
                if (SetProperty(ref _rememberPassword, value))
                {
                    MarkChanged();
                }
            }
        }

        public bool StartWithWindows
        {
            get => _startWithWindows;
            set
            {
                if (SetProperty(ref _startWithWindows, value))
                {
                    MarkChanged();
                }
            }
        }


        public string Theme
        {
            get => _theme;
            set
            {
                string normalizedTheme = ThemeService.Normalize(value);

                if (SetProperty(ref _theme, normalizedTheme))
                {
                    ThemeService.Apply(normalizedTheme);
                    MarkChanged();
                }
            }
        }

        public int RefreshIntervalSeconds
        {
            get => _refreshIntervalSeconds;
            set
            {
                if (SetProperty(ref _refreshIntervalSeconds, value))
                {
                    MarkChanged();
                }
            }
        }

        public int DefaultPauseMinutes
        {
            get => _defaultPauseMinutes;
            set
            {
                if (SetProperty(ref _defaultPauseMinutes, value))
                {
                    MarkChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set => SetProperty(ref _hasUnsavedChanges, value);
        }

        public bool NotificationsEnabled { get => _notificationsEnabled; set { if (SetProperty(ref _notificationsEnabled, value)) MarkChanged(); } }
        public bool NotificationCentreEnabled { get => _notificationCentreEnabled; set { if (SetProperty(ref _notificationCentreEnabled, value)) MarkChanged(); } }
        public bool WindowsToastsEnabled { get => _windowsToastsEnabled; set { if (SetProperty(ref _windowsToastsEnabled, value)) MarkChanged(); } }
        public bool QuietHoursEnabled { get => _quietHoursEnabled; set { if (SetProperty(ref _quietHoursEnabled, value)) MarkChanged(); } }
        public string QuietHoursStart { get => _quietHoursStart; set { if (SetProperty(ref _quietHoursStart, value)) MarkChanged(); } }
        public string QuietHoursEnd { get => _quietHoursEnd; set { if (SetProperty(ref _quietHoursEnd, value)) MarkChanged(); } }

        public IRelayCommand SaveCommand { get; }

        public IRelayCommand ReloadCommand { get; }

        public IAsyncRelayCommand TestWindowsNotificationCommand { get; }

        public SettingsViewModel(
            SettingsService settingsService,
            IRouterManagerProvider routerManagerProvider,
            AdGuardAvailabilityService adGuardAvailability,
            IToastNotificationService toastNotificationService)
        {
            _settingsService = settingsService;
            _routerManagerProvider = routerManagerProvider;
            AdGuardAvailability = adGuardAvailability;
            _toastNotificationService = toastNotificationService;

            SaveCommand =
                new RelayCommand(Save);

            ReloadCommand =
                new RelayCommand(Load);

            TestWindowsNotificationCommand =
                new AsyncRelayCommand(TestWindowsNotificationAsync);

            Load();
        }

        public void Load()
        {
            _isLoading = true;

            try
            {
                AppSettings settings =
                    _settingsService.Load();

                RouterIp =
                    settings.RouterHost;

                Username =
                    settings.Username;

                RememberPassword =
                    settings.RememberPassword;

                Password =
                    settings.RememberPassword
                        ? _settingsService.DecryptPassword(
                            settings.EncryptedPassword)
                        : "";

                StartWithWindows =
                    settings.StartWithWindows;

                Theme =
                    ThemeService.Normalize(settings.Theme);

                RefreshIntervalSeconds =
                    settings.RefreshIntervalSeconds <= 0
                        ? 30
                        : settings.RefreshIntervalSeconds;

                DefaultPauseMinutes =
                    settings.DefaultPauseMinutes <= 0
                        ? 30
                        : settings.DefaultPauseMinutes;
                NotificationPreferences preferences = settings.NotificationPreferences ?? new NotificationPreferences();
                NotificationsEnabled = preferences.Enabled;
                NotificationCentreEnabled = preferences.NotificationCentreEnabled;
                WindowsToastsEnabled = preferences.WindowsToastsEnabled;
                QuietHoursEnabled = preferences.QuietHoursEnabled;
                QuietHoursStart = preferences.QuietHoursStart.ToString("HH:mm");
                QuietHoursEnd = preferences.QuietHoursEnd.ToString("HH:mm");

                HasUnsavedChanges =
                    false;

                StatusMessage =
                    "Settings loaded.";
            }
            catch (Exception ex)
            {
                StatusMessage =
                    "Unable to load settings: " +
                    ex.Message;
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void Save()
        {
            string? validationError =
                Validate();

            if (validationError is not null)
            {
                StatusMessage =
                    validationError;

                return;
            }

            try
            {
                AppSettings existing = _settingsService.Load();

                var settings =
                    new AppSettings
                    {
                        RouterPort = existing.RouterPort,
                        AdGuardPort = existing.AdGuardPort,
                        UseRouterHttps = existing.UseRouterHttps,
                        UseAdGuardHttps = existing.UseAdGuardHttps,
                        RouterHost =
                            RouterConnectionOptions.NormaliseHost(RouterIp),

                        Username =
                            Username.Trim(),

                        RememberPassword =
                            RememberPassword,

                        EncryptedPassword =
                            RememberPassword
                                ? _settingsService.EncryptPassword(
                                    Password)
                                : "",

                        StartWithWindows =
                            StartWithWindows,

                        Theme =
                            Theme,

                        RefreshIntervalSeconds =
                            RefreshIntervalSeconds,

                        DefaultPauseMinutes =
                            DefaultPauseMinutes,
                        NotificationPreferences = new NotificationPreferences
                        {
                            Enabled = NotificationsEnabled,
                            NotificationCentreEnabled = NotificationCentreEnabled,
                            WindowsToastsEnabled = WindowsToastsEnabled,
                            QuietHoursEnabled = QuietHoursEnabled,
                            QuietHoursStart = TimeOnly.TryParse(QuietHoursStart, out TimeOnly start) ? start : new TimeOnly(22, 0),
                            QuietHoursEnd = TimeOnly.TryParse(QuietHoursEnd, out TimeOnly end) ? end : new TimeOnly(7, 0)
                        }
                    };

                _settingsService.Save(
                    settings);
                _routerManagerProvider.Invalidate();

                HasUnsavedChanges =
                    false;

                StatusMessage =
                    "Settings saved successfully.";

                SettingsSaved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                StatusMessage =
                    "Unable to save settings: " +
                    ex.Message;
            }
        }

        private async System.Threading.Tasks.Task TestWindowsNotificationAsync()
        {
            ToastDeliveryResult result = await _toastNotificationService.SendAsync(
                "RouterPilot test notification",
                "Windows notifications are working.");

            StatusMessage = result switch
            {
                ToastDeliveryResult.Delivered => "Test Windows notification sent.",
                ToastDeliveryResult.PlatformUnsupported => "Windows notifications are not supported on this platform.",
                ToastDeliveryResult.RegistrationUnavailable => "Windows notification registration is unavailable.",
                _ => "Windows notification could not be sent."
            };
        }

        private string? Validate()
        {
            if (string.IsNullOrWhiteSpace(
                    RouterIp))
            {
                return "Enter the router IP address or hostname.";
            }

            if (string.IsNullOrWhiteSpace(
                    Username))
            {
                return "Enter the SSH username.";
            }

            if (RememberPassword &&
                string.IsNullOrWhiteSpace(
                    Password))
            {
                return "Enter a password, or turn off Remember password.";
            }

            if (RefreshIntervalSeconds < 5 ||
                RefreshIntervalSeconds > 3600)
            {
                return "Refresh interval must be between 5 and 3,600 seconds.";
            }

            if (DefaultPauseMinutes < 1 ||
                DefaultPauseMinutes > 1440)
            {
                return "Default pause must be between 1 and 1,440 minutes.";
            }

            return null;
        }

        private void MarkChanged()
        {
            if (_isLoading)
            {
                return;
            }

            HasUnsavedChanges =
                true;

            StatusMessage =
                "You have unsaved changes.";
        }
    }
}
