# AdGuardTray v1.2

## Client activity
- Query totals are merged from `/control/stats` when the query log is empty.
- Client cards now explain when Last seen, Blocked and Block rate are unavailable.
- Disabled query-log values display as `Query log disabled` and em dashes rather than misleading zeroes.
- A support-page action can refresh the Clients page immediately.

## Support and diagnostics
- Renamed the About area to Support.
- Added About, Diagnostics, System, Logs and Changelog tabs.
- Added redacted router and AdGuard endpoint diagnostics.
- Added query-log status detection and a one-click repair action.
- Query-log repair preserves existing retention, anonymisation and ignored-client settings.
- Added Copy and Export diagnostics actions.
- Diagnostic exports include diagnostics, system information, build details and the support-session log.
- Added system information for Windows, .NET, process architecture, memory and configured router details.
- Added an in-session support log with copy and clear actions.

## Safety
- Passwords and Admin-Token values are never included in reports.
- Existing Analytics, Protection, Logs, Network and client sorting layouts are preserved.

# Unreleased

## Fixed
- Client cards now merge per-client query totals from `/control/stats`
  when the query-log endpoint returns an empty page.
- Query-log data remains authoritative for blocked counts and last-seen times.

## Added
- A Diagnostics tab on the About page.
- Redacted checks for authentication, configured clients, query-log entries,
  per-client statistics and query-log configuration.
- Copy-to-clipboard support for diagnostic reports.

# Changelog

## 2026-07-28 — Analytics ranks and live client-card refresh

### Fixed
- Analytics Top Clients, Top Requested Domains and Top Blocked Domains now show 1 through 10.
- Removed the invalid converter namespace approach that caused MC3000.
- Added a one-based Rank property directly to Analytics ranking items.
- Client cards refresh every 10 seconds while the Clients page is visible.
- Client data refreshes immediately whenever the page is reopened.
- Refresh stops when navigating away from the Clients page.

### Scope
- No RouterManager changes.
- No client layout, sorting, filtering or favourites changes.
- No Logs, Protection or Blocked Services files are replaced.
