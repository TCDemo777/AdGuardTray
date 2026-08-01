# AdGuardTray

[![Release](https://img.shields.io/github/v/release/TCDemo777/AdGuardTray)](https://github.com/TCDemo777/AdGuardTray/releases)
[![Build](https://github.com/TCDemo777/AdGuardTray/actions/workflows/build.yml/badge.svg)](https://github.com/TCDemo777/AdGuardTray/actions)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A Windows tray companion for monitoring and managing supported GL.iNet routers running AdGuard Home.

## Features

- Live router, AdGuard Home, network and storage status
- AdGuard protection controls, filtering options and blocked-service management
- DNS analytics, query history, live logs and client details
- Wi-Fi network and connected-client monitoring
- GL.iNet main, Guest and IoT network awareness
- Favourite clients and client intelligence
- Live upload and download traffic graphs
- Router diagnostics, ping, traceroute and DNS lookup tools
- Secure password storage using Windows user-scoped encryption
- Light, dark and system theme support
- Notification-area integration with close-to-tray behaviour

## What’s new in 1.4.0

Version 1.4.0 focuses on reliability, performance and multi-network support.

- Centralised router and AdGuard Home endpoint configuration
- Removed hard-coded router addresses
- Improved settings persistence and migration from earlier releases
- Restored reliable close/minimise-to-tray behaviour
- Reused HTTP and SSH connections to reduce latency and router load
- Prevented overlapping dashboard refreshes
- Parallelised independent AdGuard Home refresh operations
- Split the large router manager into focused partial implementation files
- Improved Wi-Fi client discovery across GL.iNet and OpenWrt data sources
- Correctly maps GL.iNet IoT and Guest clients to their matching SSIDs
- Improved handling of multiple SSIDs on the same radio
- Updated About and diagnostic version reporting

## Requirements

- Windows 10 or Windows 11
- A supported GL.iNet router reachable over the local network
- SSH access enabled on the router
- AdGuard Home installed and available on the router
- .NET 9 Desktop Runtime when using a framework-dependent build

## Getting started

1. Download the latest release.
2. Launch AdGuardTray.
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

AdGuardTray is released under the MIT Licence. See `LICENSE` and `THIRD_PARTY_NOTICES.txt` for details.
