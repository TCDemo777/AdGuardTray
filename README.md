# RouterPilot

[![Release](https://img.shields.io/github/v/release/TCDemo777/AdGuardTray)](https://github.com/TCDemo777/AdGuardTray/releases)
[![Build](https://github.com/TCDemo777/AdGuardTray/actions/workflows/build.yml/badge.svg)](https://github.com/TCDemo777/AdGuardTray/actions)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Companion for GL.iNet Routers & AdGuard Home

RouterPilot is the user-facing application name. The repository, solution,
internal project, executable and local settings folder retain the
`AdGuardTray` name for compatibility.

## Features

- Router health dashboard
- AdGuard Home protection controls and analytics
- Client monitoring and detailed client information
- Global search
- Favourite clients
- Multi-SSID, Guest and IoT network mapping
- Live WAN throughput and DNS charts
- Persistent Notification Centre
- Router online and offline notifications
- AdGuard protection-change notifications
- New-device notifications
- Persistent device history
- Router Insights with contextual actions
- Coordinated refresh scheduling and improved resource lifecycle management
- Router diagnostics, ping, traceroute and DNS lookup tools
- Secure router settings and password storage using Windows user-scoped encryption
- Light, dark and system theme support
- Minimise and close to the Windows notification area

## What’s new in 1.5.1

Version 1.5.1 brings the RouterPilot identity together with richer monitoring,
notifications and reliability improvements.

- Rebranded the user-facing application as RouterPilot while preserving internal compatibility
- Added a persistent Notification Centre
- Added router connectivity and AdGuard protection-state notifications
- Added new-device detection and persistent device history
- Added rule-based Router Insights with contextual actions
- Improved WAN and DNS graph updates and refresh performance
- Improved refresh, resource, persistence and application-shutdown stability
- Polished spacing, styling and empty states across the interface

## Requirements

- Windows 10 or Windows 11
- A supported GL.iNet router reachable over the local network
- SSH access enabled on the router
- AdGuard Home installed and available on the router
- .NET 9 Desktop Runtime when using a framework-dependent build

## Getting started

1. Download the latest Windows x64 ZIP or MSI from [GitHub Releases](https://github.com/TCDemo777/AdGuardTray/releases). The current installer is `RouterPilot-1.5.1-x64.msi`.
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
