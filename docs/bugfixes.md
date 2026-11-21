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
