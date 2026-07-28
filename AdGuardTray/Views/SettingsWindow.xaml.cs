using System;
using System.Windows;
using AdGuardTray.Models;
using AdGuardTray.Services;

namespace AdGuardTray.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;


        public SettingsWindow()
        {
            InitializeComponent();

            _settingsService =
                new SettingsService();

            LoadSettings();

            SaveButton.Click += SaveButton_Click;
        }



        private void LoadSettings()
        {
            AppSettings settings =
                _settingsService.Load();


            RouterIpBox.Text =
                settings.RouterIp;


            UsernameBox.Text =
                settings.Username;


            PasswordBox.Password =
                _settingsService.DecryptPassword(
                    settings.EncryptedPassword);


            RememberPasswordCheck.IsChecked =
                settings.RememberPassword;


            StartWithWindowsCheck.IsChecked =
                settings.StartWithWindows;
        }





        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var settings =
                    new AppSettings
                    {
                        RouterIp =
                            RouterIpBox.Text.Trim(),

                        Username =
                            UsernameBox.Text.Trim(),

                        RememberPassword =
                            RememberPasswordCheck.IsChecked == true,

                        StartWithWindows =
                            StartWithWindowsCheck.IsChecked == true
                    };



                if (settings.RememberPassword)
                {
                    settings.EncryptedPassword =
                        _settingsService.EncryptPassword(
                            PasswordBox.Password);
                }
                else
                {
                    settings.EncryptedPassword = "";
                }



                _settingsService.Save(settings);



                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Settings Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }





        private void Cancel_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}