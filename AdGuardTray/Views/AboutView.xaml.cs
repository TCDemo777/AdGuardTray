using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using AdGuardTray.Services;

namespace AdGuardTray.Views
{
    public partial class AboutView : UserControl
    {
        private readonly SettingsService _settingsService =
            new SettingsService();

        public AboutView()
        {
            InitializeComponent();
            LoadChangelog();
        }

        private void LoadChangelog()
        {
            string[] candidatePaths =
            {
                Path.Combine(
                    AppContext.BaseDirectory,
                    "CHANGELOG.md"),
                Path.Combine(
                    AppContext.BaseDirectory,
                    "..",
                    "..",
                    "..",
                    "CHANGELOG.md")
            };

            foreach (string path in candidatePaths)
            {
                string fullPath =
                    Path.GetFullPath(path);

                if (!File.Exists(fullPath))
                {
                    continue;
                }

                try
                {
                    ChangelogTextBox.Text =
                        File.ReadAllText(
                            fullPath,
                            Encoding.UTF8);

                    return;
                }
                catch (Exception ex)
                {
                    ChangelogTextBox.Text =
                        "The changelog could not be read.\n\n" +
                        ex.Message;

                    return;
                }
            }

            ChangelogTextBox.Text =
                "CHANGELOG.md was not found.\n\n" +
                "Ensure it is included as Content in AdGuardTray.csproj " +
                "with CopyToOutputDirectory enabled.";
        }

        private void ReloadChangelog_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadChangelog();
        }

        private async void RunDiagnostics_Click(
            object sender,
            RoutedEventArgs e)
        {
            DiagnosticsTextBox.Text =
                "Running diagnostics...";

            try
            {
                var settings =
                    _settingsService.Load();

                string password =
                    _settingsService.DecryptPassword(
                        settings.EncryptedPassword);

                var routerManager =
                    new RouterManager(
                        settings.RouterIp,
                        settings.Username,
                        password);

                DiagnosticsTextBox.Text =
                    await routerManager
                        .GetClientDiagnosticsAsync();
            }
            catch (Exception ex)
            {
                DiagnosticsTextBox.Text =
                    ex.ToString();
            }
        }

        private void CopyDiagnostics_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(
                    DiagnosticsTextBox.Text))
            {
                return;
            }

            Clipboard.SetText(
                DiagnosticsTextBox.Text);
        }

        private void GitHubLink_RequestNavigate(
            object sender,
            RequestNavigateEventArgs e)
        {
            Process.Start(
                new ProcessStartInfo(
                    e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });

            e.Handled = true;
        }
    }
}
