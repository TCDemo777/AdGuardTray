AdGuardTray v1.2

This ZIP contains the complete source project, based on the latest working
project used for the client-statistics and diagnostics work.

Highlights:
- Client query totals from AdGuard statistics.
- Clear unavailable-state display when query logging is disabled.
- About page upgraded to a Support page with:
  About, Diagnostics, System, Logs and Changelog.
- One-click Enable query log action.
- Run, Copy and Export diagnostics.
- Manual Refresh clients action.
- Redacted ZIP diagnostic export.
- Windows/.NET/process/memory/router system information.
- In-session support log.

Build:
1. Open AdGuardTray.csproj.
2. Build > Clean Solution.
3. Build > Rebuild Solution.
4. Run the app and open Support > Diagnostics.
5. Select Enable query log when the warning is shown.
6. Generate fresh DNS traffic; Last seen, Blocked and Block rate populate
   from new query-log entries.

Important:
Activity from while query logging was disabled cannot be reconstructed.
No password or Admin-Token value is included in diagnostics.

Suggested commit:
feat: release v1.2 support diagnostics and client activity recovery
