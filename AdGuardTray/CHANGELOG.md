# Changelog

## 2026-07-28 — Restore original working client statistics request

### Fixed
- Reverted the AdGuard query-log request used for client-card statistics to
  the exact request introduced in commit 42204c3:
  `/control/querylog?limit=5000`.
- Removed the newer paging/cursor/cache-busting request path from this method,
  because it can return an empty data array on the target GL.iNet build.

### Preserved
- Current Clients UI and five-second refresh.
- Current Analytics layout and rankings.
- Current Logs UI, Protection and Blocked Services.
