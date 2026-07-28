using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using AdGuardTray.Services;
using Microsoft.Win32;

namespace AdGuardTray.Views
{
    public partial class AboutView : UserControl
    {
        private readonly SettingsService _settingsService =
            new SettingsService();

        private readonly StringBuilder _supportLog =
            new StringBuilder();

        public AboutView()
        {
            InitializeComponent();
            LoadChangelog();
            LoadSystemInformation();
            AppendLog("Support page opened.");
        }

        private RouterManager CreateRouterManager()
        {
            var settings =
                _settingsService.Load();

            string password =
                _settingsService.DecryptPassword(
                    settings.EncryptedPassword);

            return new RouterManager(
                settings.RouterIp,
                settings.Username,
                password);
        }

        private async void RunDiagnostics_Click(
            object sender,
            RoutedEventArgs e)
        {
            DiagnosticsTextBox.Text =
                "Running diagnostics...";

            AppendLog("Diagnostics started.");

            try
            {
                string report =
                    await CreateRouterManager()
                        .GetClientDiagnosticsAsync();

                DiagnosticsTextBox.Text =
                    report;

                QueryLogWarningBorder.Visibility =
                    report.Contains(
                        "Enabled: False",
                        StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                AppendLog("Diagnostics completed.");
            }
            catch (Exception ex)
            {
                DiagnosticsTextBox.Text =
                    ex.ToString();

                AppendLog(
                    "Diagnostics failed: " +
                    ex.Message);
            }
        }

        private async void EnableQueryLog_Click(
            object sender,
            RoutedEventArgs e)
        {
            EnableQueryLogButton.IsEnabled =
                false;

            EnableQueryLogButton.Content =
                "Enabling...";

            AppendLog("Query-log repair requested.");

            try
            {
                RouterManager routerManager =
                    CreateRouterManager();

                var current =
                    await routerManager
                        .GetProtectionOptionsAsync();

                await routerManager
                    .SetQueryLogEnabledAsync(
                        true,
                        current);

                ClientRefreshNotifier.RequestRefresh();

                AppendLog(
                    "Query logging enabled; client refresh requested.");

                string report =
                    await routerManager
                        .GetClientDiagnosticsAsync();

                DiagnosticsTextBox.Text =
                    report;

                QueryLogWarningBorder.Visibility =
                    report.Contains(
                        "Enabled: False",
                        StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                DiagnosticsTextBox.Text =
                    "Unable to enable query logging.\n\n" +
                    ex;

                QueryLogWarningBorder.Visibility =
                    Visibility.Visible;

                AppendLog(
                    "Query-log repair failed: " +
                    ex.Message);
            }
            finally
            {
                EnableQueryLogButton.IsEnabled =
                    true;

                EnableQueryLogButton.Content =
                    "Enable query log";
            }
        }

        private void RefreshClients_Click(
            object sender,
            RoutedEventArgs e)
        {
            ClientRefreshNotifier.RequestRefresh();
            AppendLog("Manual client refresh requested.");
        }

        private void CopyDiagnostics_Click(
            object sender,
            RoutedEventArgs e)
        {
            CopyText(
                DiagnosticsTextBox.Text,
                "Diagnostics copied.");
        }

        private void ExportDiagnostics_Click(
            object sender,
            RoutedEventArgs e)
        {
            var dialog =
                new SaveFileDialog
                {
                    Filter = "ZIP archive (*.zip)|*.zip",
                    FileName =
                        "AdGuardTray_Diagnostics_" +
                        DateTime.Now.ToString("yyyy-MM-dd_HHmmss") +
                        ".zip"
                };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                string tempFolder =
                    Path.Combine(
                        Path.GetTempPath(),
                        "AdGuardTrayDiagnostics_" +
                        Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(
                    tempFolder);

                File.WriteAllText(
                    Path.Combine(
                        tempFolder,
                        "diagnostics.txt"),
                    DiagnosticsTextBox.Text,
                    Encoding.UTF8);

                File.WriteAllText(
                    Path.Combine(
                        tempFolder,
                        "system.txt"),
                    SystemTextBox.Text,
                    Encoding.UTF8);

                File.WriteAllText(
                    Path.Combine(
                        tempFolder,
                        "support-log.txt"),
                    _supportLog.ToString(),
                    Encoding.UTF8);

                File.WriteAllText(
                    Path.Combine(
                        tempFolder,
                        "build.txt"),
                    GetBuildInformation(),
                    Encoding.UTF8);

                if (File.Exists(dialog.FileName))
                {
                    File.Delete(
                        dialog.FileName);
                }

                ZipFile.CreateFromDirectory(
                    tempFolder,
                    dialog.FileName);

                Directory.Delete(
                    tempFolder,
                    true);

                AppendLog(
                    "Diagnostics exported to " +
                    dialog.FileName);
            }
            catch (Exception ex)
            {
                AppendLog(
                    "Diagnostics export failed: " +
                    ex.Message);

                MessageBox.Show(
                    ex.Message,
                    "Unable to export diagnostics",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RefreshSystem_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadSystemInformation();
            AppendLog("System information refreshed.");
        }

        private void LoadSystemInformation()
        {
            var settings =
                _settingsService.Load();

            long workingSet =
                Environment.WorkingSet;

            var builder =
                new StringBuilder();

            builder.AppendLine("AdGuardTray System Information");
            builder.AppendLine(
                "Generated: " +
                DateTimeOffset.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss zzz"));
            builder.AppendLine();

            builder.AppendLine("Application");
            builder.AppendLine("-----------");
            builder.AppendLine("Version: 1.2");
            builder.AppendLine(
                "Assembly: " +
                (Assembly.GetExecutingAssembly()
                    .GetName()
                    .Version?
                    .ToString() ?? "unknown"));
            builder.AppendLine(
                "Process architecture: " +
                RuntimeInformation.ProcessArchitecture);
            builder.AppendLine(
                "Memory usage: " +
                FormatBytes(
                    workingSet));
            builder.AppendLine();

            builder.AppendLine("Runtime");
            builder.AppendLine("-------");
            builder.AppendLine(
                ".NET: " +
                RuntimeInformation.FrameworkDescription);
            builder.AppendLine(
                "OS: " +
                RuntimeInformation.OSDescription);
            builder.AppendLine(
                "OS architecture: " +
                RuntimeInformation.OSArchitecture);
            builder.AppendLine(
                "64-bit process: " +
                Environment.Is64BitProcess);
            builder.AppendLine(
                "Processor count: " +
                Environment.ProcessorCount);
            builder.AppendLine();

            builder.AppendLine("Configured router");
            builder.AppendLine("-----------------");
            builder.AppendLine(
                "Address: " +
                settings.RouterIp);
            builder.AppendLine(
                "Username: " +
                settings.Username);
            builder.AppendLine(
                "Refresh interval: " +
                settings.RefreshIntervalSeconds +
                " seconds");
            builder.AppendLine(
                "Password stored: " +
                (!string.IsNullOrWhiteSpace(
                    settings.EncryptedPassword)));

            SystemTextBox.Text =
                builder.ToString();
        }

        private static string GetBuildInformation()
        {
            var assembly =
                Assembly.GetExecutingAssembly();

            return
                "AdGuardTray v1.2\n" +
                "Assembly version: " +
                (assembly.GetName().Version?.ToString() ?? "unknown") +
                "\nBuild location: " +
                AppContext.BaseDirectory +
                "\nGenerated: " +
                DateTimeOffset.Now.ToString("O");
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes =
            {
                "B",
                "KB",
                "MB",
                "GB"
            };

            double value =
                bytes;

            int index = 0;

            while (value >= 1024 &&
                   index < suffixes.Length - 1)
            {
                value /= 1024;
                index++;
            }

            return
                $"{value:F1} {suffixes[index]}";
        }

        private void CopyLog_Click(
            object sender,
            RoutedEventArgs e)
        {
            CopyText(
                SupportLogTextBox.Text,
                "Support log copied.");
        }

        private void ClearLog_Click(
            object sender,
            RoutedEventArgs e)
        {
            _supportLog.Clear();
            SupportLogTextBox.Clear();
            AppendLog("Support log cleared.");
        }

        private void CopyText(
            string text,
            string successMessage)
        {
            if (string.IsNullOrWhiteSpace(
                    text))
            {
                return;
            }

            Clipboard.SetText(
                text);

            AppendLog(
                successMessage);
        }

        private void AppendLog(string message)
        {
            _supportLog.AppendLine(
                $"[{DateTime.Now:HH:mm:ss}] {message}");

            if (SupportLogTextBox is not null)
            {
                SupportLogTextBox.Text =
                    _supportLog.ToString();

                SupportLogTextBox.ScrollToEnd();
            }
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
                    Path.GetFullPath(
                        path);

                if (!File.Exists(
                        fullPath))
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
                "CHANGELOG.md was not found.";
        }

        private void ReloadChangelog_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadChangelog();
            AppendLog("Changelog reloaded.");
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
