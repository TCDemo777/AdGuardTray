using System.Windows;

namespace AdGuardTray.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            // Read password securely:
            var password = PasswordBox.Password;
            var url = UrlTextBox.Text;
            var username = UsernameTextBox.Text;

            // TODO: Implement connection test logic
            MessageBox.Show("Testing connection...");
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Save settings to file / user settings
            MessageBox.Show("Settings saved!");

            DialogResult = true;
            Close();
        }
    }
}