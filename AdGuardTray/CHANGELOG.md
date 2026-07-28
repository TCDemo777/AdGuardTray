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
