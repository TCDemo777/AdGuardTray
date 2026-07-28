# Changelog

## 2026-07-28 — Blocked Services spacing refinement

### Changed
- Reduced the vertical gap between Blocked Services sections.
- Replaced large filled section headers with compact headings and divider lines.
- Reduced group padding while retaining clear visual separation.
- Reduced service-tile height and margins for denser scrolling.
- Preserved the three-column layout, categories and vertical scrollbar.

## 2026-07-28 — Protection and Analytics layout refinement

### Changed
- Rebuilt Blocked Services sections as compact bordered groups.
- Kept exactly three service tiles per row with more consistent spacing.
- Increased the Blocked Services viewport and restored a clear vertical scrollbar.
- Restored the Analytics page-level vertical scrollbar.
- Reworked Analytics status chips so `ALL`, `ALLOWED` and `BLOCKED` fit cleanly.
- Improved Analytics title wrapping, spacing and ranking-card alignment.

All notable changes to AdGuardTray are recorded here.

The project is a Windows WPF companion for a GL.iNet Flint 2 / GL-MT6000 router
and its embedded AdGuard Home instance.

## 2026-07-28 — Network, Protection and Analytics UI polish

### Changed
- Removed the redundant `Connected` badge beside the Router heading on Network.
- Split Network memory details into `Used` and `Cache`.
- Changed Blocked Services to a vertically scrolling, three-column card layout.
- Grouped Blocked Services into sections such as Gaming, Streaming & Video,
  Music, Social Media, Messaging & Meetings, Cloud Storage, Development,
  Shopping, AI Services, Email, Adult Content and Other.
- Added automatic fallback to the Other section for new or unknown services.
- Moved Analytics request-state labels into the top of each ranking card.
- Removed repeated Allowed/Blocked descriptions from every Analytics row.

## 2026-07-28 — Data and reliability restoration

### Fixed
- Restored the blocked-services catalogue across AdGuard Home response variants.
- Restored native Top Clients, Top Requested Domains and Top Blocked Domains.
- Prevented query-log fallback rankings from overwriting native statistics.
- Restored query-log request compatibility and cache-busting.
- Restored live Memory Used and Memory Cache updates from `/proc/meminfo`.
- Updated CPU parsing for BusyBox and standard `top` output.

### Changed
- Added friendly client names where AdGuard Home supplies device metadata.
- Restored vertical scrolling on Analytics.
- Updated Protection controls and Blocked Services actions.
- Added CSV and JSON export support for applicable data views.

## Earlier development

- Added Dashboard, Overview, Router, Internet, Network and AdGuard status views.
- Added protection enable, disable, pause and resume controls.
- Added Analytics, query history and top-list rankings.
- Added Clients and Client Details views.
- Added DNS filtering rules and DNS rewrites.
- Added Settings, About and diagnostics support.
