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

## 2. One backup time provider instead of two (TimeZoneDB not added)

- **Status:** accepted (2026-11-21).
- **Decision:** time.now is the primary provider and timeapi.io the single backup. A second backup (for example TimeZoneDB) is not added.
- **Reason (owner):** TimeZoneDB requires an API key. A second backup could lead to fewer failures, but it means another integration to handle, another API key and another API to look after. One backup is enough for this MVP.

## 3. Attendance corrections: phased implementation

- **Status:** accepted (2026-11-21).
- **Decision:** the first version supports **Add** and **Adjust** corrections. **Void** and whole-day corrections are planned for the next development phase.
- **Reason (owner):** these features are implemented separately because of the added complexity of overlapping records, audit history and data integrity.

## 5. `sa` credentials in the backend container: not changed for the MVP

- **Status:** accepted for the MVP (2026-11-21); optional production upgrade.
- **Current state:** the app runs as the least-privilege user `clockapp_app`. The `sa` connection string (`ConnectionStrings:Migration`) is still present in the backend container's environment and is used only at startup for migrations, provisioning and admin seeding.
- **Decision (owner):** not implemented for the MVP. A separate migration step that keeps the `sa` password out of the running app container is an optional future upgrade for production.
