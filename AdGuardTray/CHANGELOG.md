# Changelog

## 2026-07-28 — Reliability restoration

- Fixed Overview CPU parsing for BusyBox and standard `top` output.
- Split memory information into used memory and cache.
- Restored the working Blocked Services controls on Protection.
- Restored the Analytics page scrollbar.
- Preserved AdGuard Home top-list statistics instead of overwriting them with the query-log fallback.
- Restored the known-working query-log view and UI-thread refresh implementation.
- Added friendly client names to query-log rows when AdGuard Home provides them.

# AdGuardTray Changelog

> This changelog is cumulative. Earlier release information is retained when new updates are added.

## Version 1.0 RC — Analytics, Network and Live Logs Update
- Redesigned Analytics rankings into three spacious cards.
- Added ranking markers, contextual descriptions and clearer count badges.
- Added vertical scrolling to the Analytics page.
- Added independent scrolling to Top Clients, Top Requested Domains and Top Blocked Domains.
- Aligned Network CPU, memory and storage health indicators.
- Improved live Logs refresh by replacing the bound collection after each update.
- Added explicit no-cache headers and a cache-busting query value to AdGuard Home query-log requests.
- Increased the query-log request limit to 5,000 records.
- Preserved newest-first ordering, filters, selection and CSV/JSON export.

## Version 1.0 RC — Monitoring and Product Polish
- Global Search.
- Analytics dashboard redesign.
- Domain insights panel.
- Client favourites.
- Client intelligence and manufacturer detection.
- Improved Network page.
- Live DNS log viewer.
- CSV and JSON log export.
- Improved About page with runtime changelog.
- Performance and UI polish.
- Overview health strip and compact card layout.
- Protection controls and blocked-services compatibility.
- Startup, storage and network-health improvements.

## Version 0.9
- Protection management.
- Live clients.
- Client details.
- Redesigned Settings.
- About page.
- Network monitoring.
- Health indicators.

## Version 0.8
- Dashboard shell.
- Router overview.
- Internet information.
- AdGuard status.
- Statistics and charts.
- Navigation framework.

## Version 0.7
- SSH connectivity.
- RouterManager integration.
- Settings persistence.
- Secure password storage.

## Initial Prototype
- WPF on .NET.
- GL.iNet router connectivity.
- First successful AdGuard Home communication.

Thanks to everyone testing and providing feedback.
