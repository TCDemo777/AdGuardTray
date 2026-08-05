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
- Router diagnostics, safe diagnostic export, ping, traceroute and DNS lookup tools
- Automatic GitHub release update checks
- Secure password storage using Windows user-scoped encryption
- Light, dark and system theme support
- Notification-area integration with close-to-tray behaviour

## What’s new in 1.6.1

Version 1.6.1 improves AdGuard service scheduling, separates router monitoring from optional AdGuard enrichment and resolves installer, Wi-Fi discovery and layout regressions.

- Added scheduled AdGuard allowed-time windows and advanced single-action schedules
- Added Protection, Blocked Services and Schedules tabs to the Protection page
- Unified paired allowed-time-window creation and editing as one user-facing setting
- Improved schedule service selection using the complete shared AdGuard service catalogue
- Kept router health, WAN, Wi-Fi and connected-client monitoring available when AdGuard Home is offline
- Standardised unavailable AdGuard-derived values as clear, theme-safe `N/A` states
- Restored GL.iNet Wi-Fi discovery, Guest/IoT mapping and transient-failure data retention
- Corrected the self-contained MSI payload and launch path

The public repository is [TCDemo777/RouterPilot](https://github.com/TCDemo777/RouterPilot). For compatibility, local settings and data remain under `%LocalAppData%\AdGuardTray`.

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

User settings are stored under `%LocalAppData%\AdGuardTray`. Passwords are protected for the current Windows user.

## Building from source

```powershell
dotnet restore .\RouterPilot\RouterPilot.csproj
dotnet build .\RouterPilot\RouterPilot.csproj -c Release
```

## Support and diagnostics

The About page includes system information, redacted diagnostics, support logs and export tools. Please remove any information you do not want to share before attaching diagnostics to an issue.

Report issues through the [GitHub issue tracker](https://github.com/TCDemo777/RouterPilot/issues).

## Licence

RouterPilot is released under the MIT Licence. See `LICENSE` and `THIRD_PARTY_NOTICES.txt` for details.
