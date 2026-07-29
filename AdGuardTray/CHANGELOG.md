# AdGuardTray Changelog

## v1.3.1 — Client details and tray usability

### Fixed
- Restored Recent DNS Requests in the Client Details window by matching query-log entries against their separate client name and address fields.
- Restored Top Requested Domains and Top Blocked Domains for the selected client.
- Merged configured client IP and MAC identifiers that share the same AdGuard Home client name, allowing the MAC address to appear on a single client record.

### Added
- Added a notification-area context menu with Open Dashboard, Refresh Dashboard and Exit AdGuardTray actions.
- Added double-click support on the notification-area icon to restore the dashboard.
- Added a one-time notification explaining that AdGuardTray remains active after the dashboard is hidden.

### Changed
- Closing the dashboard with the X now hides it to the notification area instead of exiting.
- Minimising the dashboard now hides it to the notification area.
- Updated application, assembly and file versions to 1.3.1.

## v1.3 — UI polish and historical changelog

### Fixed
- Restored vertical scrolling on Analytics.
- Prevented the DNS Query History chart and ranking panels from being clipped.
- Improved Top Clients rendering when a friendly name and IP address are both present.

### Added
- Added a tasteful Support Development section linked to GitHub Sponsors.
- Added a repository Sponsor button through `.github/FUNDING.yml`.
- Added GitHub Sponsors information and badge to the README.
- Added Credits & Acknowledgements for GL.iNet, AdGuard Home, Microsoft and direct open-source dependencies.
- Added GitHub, documentation, issue-reporting and local licence actions.
- Added LICENSE and THIRD_PARTY_NOTICES.txt to release output.

### Changed
- Top Clients now displays friendly names and addresses on separate lines.
- Clients opens sorted by **Blocked queries**, **Descending**.
- Restyled Logs with a cleaner search area, alternating rows, hover states,
  improved spacing and allowed/blocked status badges.
- Rebuilt the changelog from the complete GitHub commit history.

## v1.2 — Support diagnostics and client activity recovery

### Added
- Support area with About, Diagnostics, System, Logs and Changelog tabs.
- Redacted router and AdGuard Home diagnostics.
- One-click query-log repair while preserving retention and privacy settings.
- Copy and ZIP export of diagnostic information.
- Windows, .NET, architecture, memory and configured-router information.
- In-session support logging and manual Clients refresh.

### Fixed
- Restored per-client query totals by merging `/control/stats` `top_clients`.
- Added explicit unavailable states when query logging is disabled.
- Preserved query-log data as the source for blocked counts and last-seen times.

## 2026-07-28 — Logs, protection and layout stabilisation

### Added
- Live AdGuard Home log restoration and improved Protection status updates.
- Refined Protection management controls and user feedback.

### Fixed
- Multiple live-log polling and refresh regressions.
- Analytics scrolling, chart sizing and ranking layout regressions.
- Blocked Services spacing and final layout issues.
- Newest query-log page retrieval.
- Missing Protection API paths in `RouterManager`.
- Cumulative runtime changelog loading.
- General view, analytics and log defects.

## 2026-07-27 — Search, intelligence and application-wide polish

### Added
- Global search and domain-monitoring tools.
- Client intelligence, details, favourites, manufacturer and device-type enrichment.
- Client sorting and immediate sort refresh.
- Reliable live log filters and polling.
- Complete AdGuard Home protection-management suite.
- Dedicated Protection view and navigation.
- About page, branding and application polish.
- Improved startup flow and router storage-health parsing.
- Blocked Services management and dashboard integration.

### Changed
- Refined Network resource cards and analytics health presentation.
- Populated Network page data.
- Improved Settings, client details and dashboard presentation.
- Moved AdGuard protection controls into their dedicated view.

### Fixed
- Dashboard protection state and health colours.
- Analytics ranked-item compatibility.
- Overview, Logs and Analytics layout issues.
- Generated selected-sort property handling.
- Client sorting responsiveness.
- Live log polling reliability.

## 2026-07-26 — Clients, logs and primary navigation

### Added
- Live AdGuard Home DNS query-log viewer.
- Live AdGuard client statistics.
- Clients model, view model, retrieval and complete page UI.
- Settings page and settings navigation.
- Logs page and navigation.
- Clients page navigation.
- Network page navigation.
- Overview and Analytics navigation.
- Analytics view and restored query-history chart.
- Initial README project documentation.

### Changed
- Consolidated the Clients implementation through the main branch merge.

## 2026-07-25 — Dashboard and analytics foundations

### Added
- Dashboard navigation shell.
- Dashboard header actions.
- LiveCharts query-history binding.
- Query-history parsing from AdGuard Home.
- Early graph implementations.
- Router RPC hash authentication.
- Initial dashboard statistics and data flow.

## 2026-07-24 — Router and AdGuard connectivity

### Added
- Working GL.iNet router API access.
- Working SSH connection and dashboard integration.
- Settings-aware connection recovery.
- AdGuard Home API connectivity.
- Progressive RPC hash-authentication support.

## 2026-07-23 — Application shell

### Added
- Successful router login page.
- Working Windows tray application.

## 2026-07-22 — Project creation

### Added
- Initial WPF project.
- Base project files.
- Repository attributes and ignore rules.
