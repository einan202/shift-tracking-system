# Bug fixes

## Intermittent "time service is unavailable" on Clock In / Clock Out

### Problem
Clock In / Clock Out sometimes failed with *"The time service is unavailable. Nothing was recorded; please try again."* (HTTP 503), and a second click usually worked.
Reproduce (original build, single provider timeapi.io): press Clock in / Clock out repeatedly, or loop `POST /api/attendance/clock-in` / `clock-out` with an employee token. In a 40-punch run, 8 punches (20%) returned 503.
Data was never affected: every failed punch wrote no row (checked in the database).

### Root cause
The failures came from the only time provider, timeapi.io, not from the frontend, the backend code or the database. Evidence:
- Backend logs: 18 rejected punches. 6 were `timeapi.io returned HTTP 502`, 12 were `HttpClient.Timeout of 3 seconds elapsing` (`TaskCanceledException`).
- Direct requests to the provider, no app involved (90 requests): 67% HTTP 200, 33% HTTP 502 (body: nginx "502 Bad Gateway"). Among the 200s: p50 1.2 s, p90 3.6 s, max 18 s, so many good answers exceeded the app's 3 s timeout.
- More retries against the same provider did not fix it (measurements below): 8 attempts still failed 3 of 60 punches and was slower. Failures were not independent per attempt; the cause of that clustering was not investigated.
- The alternative provider time.now answered 60 of 60 direct requests with HTTP 200 (p50 0.20 s, max 0.34 s).

### Solution
- `ResilientTimeSource` (new): providers tried in order, each with a small bounded number of attempts: **time.now x2, then timeapi.io x2** (at most 4 requests per punch, 2 s / 3 s timeouts, 150 ms pause). If every provider fails the request fails with 503 and nothing is written; there is still no browser or server clock fallback.
- `TimeNowTimeSource` (new): reads `utc_datetime`, `utc_offset`, `dst`, `timezone`, `unixtime` from time.now. It requires `Europe/Zurich`, an explicit UTC timestamp, `unixtime` agreeing with `utc_datetime`, and DST flag and offset consistent with the Europe/Zurich rules (`ZurichTime.FromUtc`). Any mismatch is a failed attempt.
- `TimeApiIoTimeSource` is now a single attempt; retry and fallback live in one place.
- The provider that supplied the time is stored in `AttendanceRecords.TimeProvider`.
- Retrying is safe because re-reading the time is a read-only GET done before anything is written; a punch is still stored once, with the time of the one successful answer.
- Browser request timeout raised 10 s to 15 s, above the backend worst case (about 11 s), so a slow successful punch is not cut off by the browser.

### Verification
Same 40-60 punch loop (alternating clock-in / clock-out on a throw-away employee, live stack, real providers):

| Configuration | Punches | Success | Median | Slowest |
|---|---|---|---|---|
| Original: timeapi.io, 2 attempts | 40 | 80% (8x 503) | 1.5 s | 6.0 s |
| timeapi.io, 5 attempts | 40 | 95% (2x 503) | 1.5 s | 16.1 s |
| timeapi.io, 8 attempts | 60 | 95% (3x 503) | 2.6 s | 24.8 s |
| **time.now first, timeapi.io fallback (2 + 2)** | 60 | **100%** | **0.2 s** | **0.4 s** |

Correctness of the fallback and fail-closed paths (live containers, provider URLs overridden to an unreachable address):
- time.now unreachable: clock-in returned 200 and the row stored `TimeProvider = timeapi.io`.
- Both unreachable: clock-in returned 503 and 0 rows were written.

Automated: new unit tests for both adapters (real-shaped response, wrong zone, DST/offset contradictions, UTC-designator and `unixtime` checks, single attempt), `ResilientTimeSource` (primary only, retry, fallback, bounded attempts, fail-closed, cancellation) and `ZurichTime.FromUtc` (winter, summer, both fall-back occurrences, spring-forward). Opt-in live tests (`RUN_LIVE_TIME_API=1`) passed for both providers.

### Open points
- time.now documents no numeric rate limit that I could find, and 60 sequential requests were not throttled. A normal punch makes 1 request; the worst case is 4.
- The time.now page asks for attribution ("Support This Free API (Required)"). Not done; decide before production use.

## "Add missing record" defaulted to the browser's time zone and rejected it as a future time

### Problem
In the admin "Add missing record" dialog the date/time pickers started from the admin's browser clock (Israel, UTC+2 when this was reported). Zurich was one hour behind (13:54 vs 14:54 in the check), so a time picked from the default was later than "now" in Zurich and the server answered "Clock-in/Clock-out cannot be in the future".

### Root cause
The dialog's `datetime-local` inputs for a new record had no value and no limit, so the browser filled in and offered its own local time. The server validates against the real instant in UTC, so any value typed as Zurich time that is later than Zurich now is correctly rejected. The UI never told the form what Zurich "now" is.

### Solution
- `GET /api/admin/attendance` now returns `zurichNow` (server time converted to Europe/Zurich, `yyyy-MM-ddTHH:mm`; used only for UI defaults, nothing is recorded).
- The dialog defaults to a normal working day on the Zurich date (08:00 to 17:00, never later than Zurich now) and shows "It is now <date> <time> in Zurich". The browser's time zone is no longer used.
- The Zurich "now" is re-fetched every time the dialog is opened (not just when the page loaded).
- Server-side validation is unchanged, but the "future" error now says what was compared, e.g. *"Clock-out 2026-11-21 16:00 is in the future. The current Zurich time is 2026-11-21 14:09; enter a time at or before it."* (was: "Clock-out cannot be in the future."), so the admin does not have to guess the current Zurich time.

**Follow-up (the problem still occurred after the first fix).** The first version also set `max = zurichNow` on both inputs, using the time from when the Attendance page loaded. After the page had been open for a while that cap was stale: in Chrome, 90 s after load, the default said 13:57 while Zurich was 13:59, and saving 13:59 was blocked in the browser with "Value must be 21/11/2026, 13:57 or earlier", although 13:59 was valid. Fix: removed the browser-side `max` (the server already rejects genuinely future times) and reload the Zurich "now" on every dialog open.

### Verification
- Unit tests: `ZurichTime.ToLocal` (including 22:30 UTC becoming 00:30 the next day), the `zurichNow` field on the endpoint, and the default-times helper (late evening, afternoon, early morning, just after midnight).
- Live, in Chrome with the machine in Israel time (14:54 Israel, 13:54 Zurich): the dialog opened with 08:00 and 13:54 and the note "It is now 2026-11-21 13:54 in Zurich". Saving those defaults produced only an "overlaps record" message for existing records, with no "future" error.
- Follow-up, live in Chrome: reproduced the stale cap (above), then after the fix left the page idle for about 70 s, opened the dialog (default 14:01, matching real Zurich time 14:01) and submitted 14:01 as clock-out: no browser block, only the server's "times overlap" message for an existing record (used on purpose so nothing is saved).
- Unit tests: dialog defaults come from the Zurich time it is given, and the inputs have no `max` (regression test).

## PR review fixes: correction targets, clock-out vs corrected clock-in, lock errors, open-record invariant

### Problem
- An `Adjust` correction with no `attendanceRecordId` was accepted (HTTP 201) when the employee had any manual (Add) entry, and stored as an `Adjust` row with `AttendanceRecordId = NULL`: an audit row that claims a change but changes nothing. Reproduced live.
- Clock-out was checked only against the original clock-in. If an admin had moved an open record's clock-in to a later time, a clock-out time between the two was stored, giving a record that ends before it starts.
- A timed-out per-user lock (another punch or correction holding it) ended as an unhandled 500.
- Nothing in the database stopped `IsOpen = 1` together with a `ClockOutUtc`.

### Root cause
- `CorrectionService`: `e.RecordId == req.AttendanceRecordId` is also true for manual entries (`null == null`).
- `AttendanceService.ClockOutAsync` compared the API time with `AttendanceRecords.ClockInUtc` only, not with the effective (corrected) clock-in.
- `UserLock` threw `InvalidOperationException`, which the punch endpoint did not map.
- The invariants lived only in application code.

### Solution
- `Adjust` must reference a record (422 otherwise); new DB check `CK_AttendanceCorrections_KindTarget` (Add has no target, Adjust has one).
- Clock-out is rejected with the existing fail-closed 503 when the API time is not after the effective clock-in; the record stays open.
- `UserLock` throws `AttendanceBusyException`; punches and corrections answer a retryable 503 (`Retry-After` on punches) and write nothing.
- New DB check `CK_AttendanceRecords_OpenHasNoOut`. Both checks are in migration `AddIntegrityChecks`.

### Verification
- Regression tests: Adjust without record id (with an existing manual entry), clock-out before the corrected clock-in, busy lock gives 503 then succeeds after release, and both new DB checks (accepted and rejected shapes). The first three were run against the unfixed code and fail there.
- Full backend suite 112 passed, 2 skipped (opt-in live tests).
- Live: the Adjust reproduction now returns 422 and stores no extra row; the migration applied and both constraints exist in the running database.
- Data note: the dev database contained one such invalid `Adjust` row, created by the reproduction itself; it was deleted before the migration so the constraint could be added. Any existing database with such rows would need them reviewed first.

