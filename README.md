# RouterPilot

[![Release](https://img.shields.io/github/v/release/TCDemo777/RouterPilot)](https://github.com/TCDemo777/RouterPilot/releases)
[![Build](https://github.com/TCDemo777/RouterPilot/actions/workflows/build.yml/badge.svg)](https://github.com/TCDemo777/RouterPilot/actions)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Companion for GL.iNet Routers & AdGuard Home

## Features

- Live router, AdGuard Home, network and storage status
- AdGuard protection controls, filtering options and blocked-service management
- Scheduled AdGuard blocked-service controls, including paired allowed-time windows
- DNS analytics, query history, live logs and client details
- Wi-Fi network and connected-client monitoring
- GL.iNet main, Guest and IoT network awareness
- Favourite clients, persistent device history and connection activity
- Live and historical WAN throughput, DNS, CPU and memory charts
- Network Timeline, Router Insights, weekly summaries and deterministic network intelligence
- Persistent Notification Centre with router, AdGuard and new-device events
- Configurable Windows notifications, Notification Centre delivery and quiet hours
- Maintenance Centre with safe router actions, shared action history and diagnostics
- Portable `.rpb` Backup & Restore with manifest validation and pre-restore backups
- Router diagnostics, safe diagnostic export, ping, traceroute and DNS lookup tools
- Automatic GitHub release update checks
- Secure password storage using Windows user-scoped encryption
- Light, dark and system theme support
- Notification-area integration with close-to-tray behaviour

## What's new in 1.8.0

Version 1.8.0 adds local maintenance, configurable Windows notifications and safe portable backup and restore while preserving RouterPilot's existing router and AdGuard Home monitoring.

- Added the Maintenance Centre with supported, confirmation-gated router actions and shared maintenance history
- Added verified AdGuard Home restart handling and a shared diagnostics workflow for About and Maintenance
- Added Windows notification delivery, channel preferences, quiet hours and Send Test Notification
- Added `.rpb` configuration backups with manifests, SHA-256 validation, selective restore and automatic pre-restore backups
- Improved maintenance, notification-preference, backup/restore and About-page presentation
- Preserved RouterPilot data migration, GL.iNet Wi-Fi discovery and AdGuard-independent router monitoring

The public repository is [TCDemo777/RouterPilot](https://github.com/TCDemo777/RouterPilot). RouterPilot now uses `%LocalAppData%\RouterPilot`; on first startup it safely copies supported legacy files from `%LocalAppData%\AdGuardTray` without changing or deleting the legacy folder.

## Requirements

- Windows 10 or Windows 11
- A supported GL.iNet router reachable over the local network
- SSH access enabled on the router
- AdGuard Home installed on the router for DNS filtering, query activity and protection controls
- .NET 9 Desktop Runtime when using a framework-dependent build

## Getting started

1. Download the latest release.
2. Launch RouterPilot.
3. Enter the router IP address or hostname, SSH username and password.
4. Keep **Remember password securely** enabled for automatic startup.
5. Open the dashboard from the notification-area icon.

User settings are stored under `%LocalAppData%\RouterPilot`. Passwords are protected for the current Windows user. Existing supported settings, notification, client-profile and AdGuard schedule files are copied automatically from `%LocalAppData%\AdGuardTray` when no RouterPilot replacement exists.

Release assets are published as `RouterPilot-1.8.0-x64.msi` and `RouterPilot-1.8.0-win-x64.zip`.

When upgrading from v1.7.0, install the MSI or replace the portable application files. Existing `%LocalAppData%\RouterPilot` data remains in place. Backup files use the portable `.rpb` format and can be created or restored from Maintenance.

## Building from source

```powershell
dotnet restore .\RouterPilot.sln
dotnet build .\RouterPilot.sln -c Release
```

The application executable is `RouterPilot.exe`.

## Support and diagnostics

The About page includes system information, redacted diagnostics, support logs and export tools. Please remove any information you do not want to share before attaching diagnostics to an issue.

Report issues through the [GitHub issue tracker](https://github.com/TCDemo777/RouterPilot/issues).

## Licence

RouterPilot is released under the MIT Licence. See `LICENSE` and `THIRD_PARTY_NOTICES.txt` for details.
