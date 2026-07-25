using System;
using System.Windows;
using AdGuardTray.Models;
using AdGuardTray.Services;

namespace AdGuardTray.Views
{
    public partial class DiagnosticsWindow : Window
    {
        private readonly SettingsService _settingsService;

        public DiagnosticsWindow()
        {
            InitializeComponent();

            _settingsService =
                new SettingsService();
        }

        private RouterManager CreateRouterManager()
        {
            var settings =
                _settingsService.Load();

            return new RouterManager(
                settings.RouterIp,
                settings.Username,
                _settingsService.DecryptPassword(
                    settings.EncryptedPassword));
        }

        private async void RouterInfoButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OutputBox.Text =
                "Loading router information...";

            try
            {
                RouterInfo info =
                    await CreateRouterManager()
                        .GetRouterInfoAsync();

                OutputBox.Text =
$@"Router Information

Model
------
{info.Model}

Hostname
--------
{info.Hostname}

Firmware
--------
{info.Firmware}

Uptime
------
{info.Uptime}

CPU
---
{info.CpuUsage}

Memory
------
{info.MemoryUsage}

Storage
-------
{info.StorageUsage}

WAN IP
------
{info.WanIp}

Gateway
-------
{info.Gateway}

DNS
---
{info.DnsServer}

Latency
-------
{info.Latency}";
            }
            catch (Exception ex)
            {
                OutputBox.Text =
                    ex.ToString();
            }
        }

        private async void AdGuardStatusButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OutputBox.Text =
                "Checking AdGuard Home...";

            try
            {
                var status =
                    await CreateRouterManager()
                        .GetAdGuardStatusAsync();

                OutputBox.Text =
$@"AdGuard Home Status

Running
-------
{status.IsRunning}

Service
-------
{status.ServiceStatus}

Version
-------
{status.Version}

Process
-------
{status.Process}";
            }
            catch (Exception ex)
            {
                OutputBox.Text =
                    ex.ToString();
            }
        }

        private async void LogsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OutputBox.Text =
                "Loading logs...";

            try
            {
                OutputBox.Text =
                    await CreateRouterManager()
                        .GetLogsAsync();
            }
            catch (Exception ex)
            {
                OutputBox.Text =
                    ex.ToString();
            }
        }

        private async void RestartButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OutputBox.Text =
                "Restarting AdGuard Home...";

            try
            {
                await CreateRouterManager()
                    .RestartAdGuardAsync();

                OutputBox.Text =
                    "AdGuard Home restarted successfully.";
            }
            catch (Exception ex)
            {
                OutputBox.Text =
                    ex.ToString();
            }
        }

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            OutputBox.Clear();
        }
    }
}