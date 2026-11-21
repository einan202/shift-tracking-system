# Decisions

Decisions and deferred items, recorded only after the owner has chosen them. Items not listed here are still open.

## 1. Time provider order: time.now first, timeapi.io as fallback

- **Status:** accepted (2026-11-21).
- **Plan said:** timeapi.io as the only provider (TimeZoneDB as a later fallback).
- **Decision:** `ResilientTimeSource` asks time.now first (2 attempts), then timeapi.io (2 attempts). If both fail, the punch is a 503 and nothing is written.
- **Reason (measured, details in `docs/bugfixes.md`):**
  - timeapi.io alone answered HTTP 502 for 33% of 90 direct requests, and many 200 answers took longer than the 3 s timeout. With 2 attempts, 8 of 40 punches (20%) were rejected; 5 and 8 attempts still rejected about 5%.
  - time.now answered 60 of 60 direct requests (median 0.2 s). With time.now first and timeapi.io as fallback, 60 of 60 punches succeeded (median 0.2 s).
- **Unchanged:** attendance time still comes only from external Europe/Zurich time APIs, never from the browser or server clock; the provider used is stored in `AttendanceRecords.TimeProvider`.
- **Open points:** time.now publishes no numeric rate limit and asks for attribution (not added).
