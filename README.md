# RouterPilot

[![Release](https://img.shields.io/github/v/release/TCDemo777/AdGuardTray)](https://github.com/TCDemo777/AdGuardTray/releases)
[![Build](https://github.com/TCDemo777/AdGuardTray/actions/workflows/build.yml/badge.svg)](https://github.com/TCDemo777/AdGuardTray/actions)
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
- Router diagnostics, safe diagnostic export, ping, traceroute and DNS lookup tools
- Automatic GitHub release update checks
- Secure password storage using Windows user-scoped encryption
- Light, dark and system theme support
- Notification-area integration with close-to-tray behaviour

## What’s new in 1.6.0

Version 1.6 adds local historical data, scheduled AdGuard service controls and richer network insights while preserving RouterPilot's local-first architecture.

- Added a SQLite-backed historical data platform alongside existing JSON configuration
- Added persistent device connection events and recent activity in Client Details
- Added historical WAN usage and router CPU/memory analytics with retention and downsampling
- Added weekly network summaries, Network Timeline, Router Insights and deterministic network intelligence
- Added privacy-aware diagnostic ZIP export and an automatic GitHub release update checker
- Added scheduled AdGuard blocked-service controls with daily, selected-day and one-time recurrence
- Added paired allowed-time windows, safe read-modify-write updates and schedule notifications
- Polished RouterPilot's dashboard, analytics, notifications, protection, settings and About experiences
- Restored compatible GL.iNet Wi-Fi discovery while retaining Guest, IoT and virtual-interface mapping

## Requirements

- Windows 10 or Windows 11
- A supported GL.iNet router reachable over the local network
- SSH access enabled on the router
- AdGuard Home installed and available on the router
- .NET 9 Desktop Runtime when using a framework-dependent build

## Getting started

1. Download the latest release.
2. Launch RouterPilot.
3. Enter the router IP address or hostname, SSH username and password.
4. Keep **Remember password securely** enabled for automatic startup.
5. Open the dashboard from the notification-area icon.

User settings are stored under `%LocalAppData%\AdGuardTray`. Passwords are protected for the current Windows user.

## Building from source

```powershell
dotnet restore .\AdGuardTray\AdGuardTray.csproj
dotnet build .\AdGuardTray\AdGuardTray.csproj -c Release
```

## Support and diagnostics

The About page includes system information, redacted diagnostics, support logs and export tools. Please remove any information you do not want to share before attaching diagnostics to an issue.

Report issues through the [GitHub issue tracker](https://github.com/TCDemo777/AdGuardTray/issues).

## Licence

RouterPilot is released under the MIT Licence. See `LICENSE` and `THIRD_PARTY_NOTICES.txt` for details.
