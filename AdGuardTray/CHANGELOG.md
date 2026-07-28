# Changelog

## 2026-07-28 — Analytics scrolling and reliable client refresh

### Fixed
- Restored the Analytics page's vertical side scrollbar.
- Prevented the DNS Query History graph from being compressed or clipped.
- Kept the existing 1–10 ranking display unchanged.
- Client refresh is now driven by page visibility rather than relying only on Loaded/Unloaded events.
- Client cards refresh every five seconds while the Clients page is visible.
- Returning to the Clients page triggers an immediate refresh.
- The Clients status line now shows the most recent refresh time.

### Scope
- No RouterManager changes.
- No Logs, Protection or Blocked Services changes.
- No Analytics ranking or data-loading changes.
