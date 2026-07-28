# Changelog

## 2026-07-28 — Live Logs restoration and instant protection state

### Fixed
- Restored the previously known-working Live Logs view and refresh loop.
- Restored the compatible AdGuard Home query-log request parameters.
- Kept the current RouterManager analytics and protection functionality.
- Added immediate Overview status updates after enable, disable, resume or pause.
- The Overview no longer waits for its normal polling interval after a protection command.

## 2026-07-28 — Responsive Blocked Services filter layout

### Changed
- Reduced the category selector to a compact 138-pixel filter.
- Replaced the rigid service grid with a responsive wrapping layout.
- Gave every filtered service tile a consistent width, height and spacing.
- Removed repeated category badges from service tiles.
- Added the active category beside the selection summary.
- Preserved search, category filtering, blocked-only filtering and save behaviour.

## 2026-07-28 — Blocked Services category-filter redesign

### Changed
- Replaced vertically stacked service-category sections with a category selector.
- Displays one continuous three-column service grid with no inter-category gaps.
- Added an `All categories` option and category-specific filtering.
- Added compact category badges to service tiles.
- Search now also matches category names.
- Preserved blocked-only filtering, selection commands and save behaviour.

## 2026-07-28 — Blocked Services pixel-scrolling fix

### Fixed
- Switched the grouped Blocked Services list from logical item scrolling to pixel scrolling.
- Disabled grouped-list virtualization that could reserve or jump across oversized group containers.
- Removed the remaining margin between category containers.
- Tightened the category heading and first service row spacing.

## 2026-07-28 — Compact Blocked Services sections

### Changed
- Removed the outer card around every service category.
- Reduced category spacing to two pixels.
- Tightened category headings and divider lines.
- Reduced service-tile height and vertical margins.
- Kept three service tiles per row and vertical scrolling.

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
