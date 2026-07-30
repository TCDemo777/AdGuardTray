using System;
using AdGuardTray.Models;
using AdGuardTray.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdGuardTray.ViewModels
{
    public class SettingsViewModel : ObservableObject
    {
        public event EventHandler? SettingsSaved;
        private readonly SettingsService _settingsService;

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

        public IRelayCommand SaveCommand { get; }

        public IRelayCommand ReloadCommand { get; }

        public SettingsViewModel()
        {
            _settingsService = new SettingsService();

            SaveCommand =
                new RelayCommand(Save);

            ReloadCommand =
                new RelayCommand(Load);

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
                    settings.RouterIp;

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
                var settings =
                    new AppSettings
                    {
                        RouterIp =
                            RouterIp.Trim(),

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
                            DefaultPauseMinutes
                    };

                _settingsService.Save(
                    settings);

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
