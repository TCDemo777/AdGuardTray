using System.Windows;
using System.Windows.Controls;
using AdGuardTray.ViewModels;

namespace AdGuardTray.Views
{
    public partial class SettingsView : UserControl
    {
        private readonly SettingsViewModel _viewModel;
        private bool _isUpdatingPassword;

        public SettingsView()
        {
            InitializeComponent();

            _viewModel =
                new SettingsViewModel();

            DataContext =
                _viewModel;

            Loaded +=
                SettingsView_Loaded;
        }

        private void SettingsView_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            UpdatePasswordBox();
        }

        private void PasswordInput_PasswordChanged(
            object sender,
            RoutedEventArgs e)
        {
            if (_isUpdatingPassword)
            {
                return;
            }

            _viewModel.Password =
                PasswordInput.Password;
        }

        private void UpdatePasswordBox()
        {
            _isUpdatingPassword =
                true;

            PasswordInput.Password =
                _viewModel.Password;

            _isUpdatingPassword =
                false;
        }
    }
}
