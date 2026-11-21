# Plan (revised): Clock in/out web app — ASP.NET Core + SQL Server + React, built with Claude Code

## Decisions at a glance
- **Stack (mandatory):** ASP.NET Core 10 (LTS) · SQL Server 2022 · React + Vite + TS; 3 containers (frontend nginx, backend, db on host port 1434).
- **Time:** Clock In/Out times come only from an external Europe/Zurich API (TimeAPI.io, single adapter; fail-closed 503, no clock fallback); stored as UTC with explicit DST handling. Server clock is used only for auth, correction `CreatedAtUtc` and the report's default month.
- **Enforcement:** separate `ClockApp.Attendance` project with banned-API analyzer, no-DB-defaults test, stub-API tests, frontend lint rule.
- **Auth:** Admin/Employee roles; admin creates employee → one-time invite token (only its hash stored) → employee sets own password; `PasswordHasher` for passwords.
- **Missing/duplicate punches:** Option A (strict reject, DB unique index); upgrade to C is ~1.5–2.5 h.
- **Crash/retry handling:** UI guard + `/status` check; idempotency keys deferred.
- **Corrections:** append-only `AttendanceCorrections` (Add/Adjust, mandatory reason, audit); original punches never edited.
- **Edge cases:** overnight shift = one record counted on the clock-in day; double clock-out = atomic conditional update (first wins, second 409); corrections must not overlap other effective records (per-user lock).
- **Default shift:** duration only (no start time). Employee monthly report shows daily total vs default duration.
- **Schedule:** 6–8 h; trimmed items are listed under "Deferred features".

## Context
Greenfield repo (`/Users/sagit/Desktop/shift-tracking-system`; git init + earlier research only). Fixed technologies: **ASP.NET Core (backend), Microsoft SQL Server (DB), React (frontend)** — nothing else added to the stack. Employees log in and clock in/out; Admins register employees, set the default shift **duration** (the default shift is defined by duration only, no start time) and correct attendance. Core rule: **every Clock In / Clock Out event gets its time exclusively from an external Europe/Zurich time API**. Missing/duplicate punches: **Option A (strict reject)**, upgradable to C. This revision: realistic schedule (quality over quantity, no forced 6 h), .NET version verified, existing SQL Server accounted for, DST handling made explicit, **manual corrections separated from real attendance events with mandatory reason + audit trail**, clock rule scoped to attendance events and enforced by architecture + tests.

## Stack pros / cons compared to alternatives
**Constraint:** ASP.NET Core, Microsoft SQL Server and React are mandatory. Alternatives are listed only to show the accepted trade-offs; none will be used.

**Backend**
| Option | Pros | Cons | Status |
|---|---|---|---|
| **ASP.NET Core 10 (LTS)** | Typed; built-in auth, DI, validation, rate limiting; `PasswordHasher`; first-class EF Core + SQL Server; fast | More boilerplate than Node/Python | **Required — used** |
| NestJS (Node/TS) | Same language as frontend; quick scaffolding | Weaker DB typing; SQL Server support less mature | Not used |
| Django / FastAPI (Python) | Very fast to write | Slower runtime; SQL Server drivers less smooth | Not used |
| Spring Boot (Java) | Mature, enterprise-grade | Heaviest setup | Not used |

**Database**
| Option | Pros | Cons | Status |
|---|---|---|---|
| **Microsoft SQL Server 2022** | Filtered unique indexes (DB-enforced "one open record"); `datetimeoffset`; strong transactions; best EF Core integration | Heavier container; Developer edition is non-production | **Required — used** |
| PostgreSQL | Free in production; light | Not permitted by the constraint | Not used |
| MySQL / MariaDB | Light, widely hosted | No filtered unique index (workaround needed) | Not used |
| SQLite | Zero setup | Not a separate service; breaks the 3-container design | Not used |

**Frontend**
| Option | Pros | Cons | Status |
|---|---|---|---|
| **React + Vite + TypeScript** | Fast dev loop; huge ecosystem; easy to generate/test (Vitest) | Auth/state wiring is on us | **Required — used** |
| Blazor | One language end to end | Smaller ecosystem; heavier runtime | Not used |
| Angular | Batteries included | More boilerplate | Not used |
| Next.js | SSR/routing built in | SSR unnecessary here | Not used |

## External time API (used only for Clock In/Out events)
| # | API | Free tier (re-verify in the spike) | Docs |
|---|---|---|---|
| 1 (primary) | **TimeAPI.io** `GET /api/time/current/zone?timeZone=Europe/Zurich` | Free; sources disagree on key requirement/limit (~1 req/s reported) | https://timeapi.io (Swagger https://timeapi.io/swagger/index.html) |
| 2 (fallback) | **TimeZoneDB** `GET /v2.1/get-time-zone?key=KEY&by=zone&zone=Europe/Zurich&format=json` | Free key, 1 req/s, no total cap, **non-commercial only** | https://timezonedb.com/api |
| 3 | **WorldTimeAPI** `GET /api/timezone/Europe/Zurich` (returns UTC datetime, unixtime, offset, DST flag) | No key, no numeric limit, fair-use 429s, no SLA, historically flaky | https://worldtimeapi.org/pages/examples · https://worldtimeapi.org/pages/faqs |
| alt | ipgeolocation.io Time Zone API | 1,000 credits/day free | https://ipgeolocation.io/documentation/timezone-api.html |
Implement #1 behind `IAttendanceTimeSource` now (#2 fallback adapter deferred to fit 6–8 h); #3 is a drop-in adapter if needed (it is the cleanest data source because it returns the UTC instant and offset directly — decide primary vs fallback in the spike).

**Rules (fail-closed):** the API call happens inside the Clock In/Out request; API unreachable/invalid → HTTP 503, **nothing is written, no fallback to any local clock**. Each call is short (3 s timeout, one retry, then next provider).

**Correct DST handling:** the stored truth is a **UTC instant** (`datetimeoffset`), never a naive local time.
1. Adapter normalises each provider to `{ UtcInstant, LocalDateTime, UtcOffset, IsDst, Provider }`. If the API returns the UTC time/offset use it; if it returns only local time + DST flag (TimeAPI.io), derive the UTC instant from local time + `dstActive`, which also **resolves the ambiguous hour** on fall-back day (02:00–03:00 occurs twice on 2026-10-25).
2. **Validation**: offset must be +01:00 (CET) or +02:00 (CEST) and must equal what the .NET `TimeZoneInfo` "Europe/Zurich" rules say for that instant (`TimeZoneInfo` is used only as a *cross-check of rules*, not as a clock) → otherwise 503 "time source inconsistent".
3. `WorkDate` = Zurich local date of the clock-in. Durations are UTC differences, so 23 h / 25 h DST days compute correctly.
4. Admin-entered corrections arrive as Zurich local time: **nonexistent** (spring-forward gap 02:00–03:00 on 2026-03-29) and **ambiguous** (fall-back hour) local times are rejected unless the UI supplies the offset; stored as UTC.
5. The Phase-0 spike records real responses from each API (including DST edge samples where possible) as test fixtures.

## Architecture (3 containers, docker compose)
```
Browser ─► [frontend: nginx + React build :3000] ──/api/*──► [backend: ASP.NET Core 10 :8080] ──EF Core──► [db: SQL Server 2022 (host 1434) + volume]
                                                                    └──HTTPS──► external time API (Clock In/Out only)
```
- `frontend`: multi-stage (node build → nginx), proxies `/api` to `backend` (no CORS).
- `backend`: multi-stage (`sdk:10.0` → `aspnet:10.0`); env: connection string, `JWT_SECRET`, `TIMEZONEDB_KEY`; applies migrations + seeds first admin on startup (dev); retries until DB is ready.
- `db`: the existing `mcr.microsoft.com/mssql/server:2022-latest` image, own volume and database `ClockApp`, host port 1434 (1433 is used by another local project), `MSSQL_SA_PASSWORD` in `.env`, `sqlcmd` healthcheck.
- .NET 10 is the current LTS (.NET 8/9 went out of support on 2026-11-10): https://dotnet.microsoft.com/en-us/platform/support/policy. No local `dotnet` SDK is installed, so build/test run in the `sdk:10.0` container.
- Solution layout (kept small, but split where it enforces the clock rule):
  - `ClockApp.Attendance` — entities, `DbContext`, `AttendanceService` (clock events + corrections), report calculator, `IAttendanceTimeSource`. **Banned-API analyzer enabled here.**
  - `ClockApp.Api` — controllers, auth/JWT, invite flow, time-API adapters (HttpClient), DI. May use normal system clocks for auth/tokens.
  - `ClockApp.Tests` — unit + integration tests.

### Clock rule scope and enforcement (architecture + tests)
Rule (from the requirement): every Clock In/Out record uses the external Europe/Zurich API time, and **neither the browser clock nor the server clock may be used for recording attendance** (Clock In/Out records). Non-attendance system operations — JWT/invite expiry, login lockout, logging — may use normal clocks, and live only in `ClockApp.Api`, never in the attendance project.
- **Architecture**: `AttendanceService` depends only on `IAttendanceTimeSource`; the only implementation of the *recording* time is the external-API adapter. The domain project has **no** `System.TimeProvider` registration. Clients never send timestamps for clock events (`POST /clock-in` and `/clock-out` have no body).
- **Static guard**: `Microsoft.CodeAnalysis.BannedApiAnalyzers` in `ClockApp.Attendance` bans `DateTime.Now/UtcNow/Today`, `DateTimeOffset.Now/UtcNow`, `TimeProvider.GetUtcNow/GetLocalNow`, `Stopwatch` timestamps (build fails).
- **DB guard**: test queries the schema and fails if any attendance table column has a default constraint (`GETDATE()/SYSUTCDATETIME()` etc.) — EF `HasDefaultValueSql` cannot sneak in.
- **Behaviour tests**: (a) integration test with a stub time-API server returning `2031-07-04T09:00:00+02:00` while system clock is real → stored `ClockInUtc` equals the stub value; (b) API down → 503 and zero rows written; (c) request carrying a forged timestamp/header is ignored; (d) inconsistent offset rejected; (e) fall-back/spring-forward samples compute correct UTC and `WorkDate`.
- **Frontend guard**: ESLint `no-restricted-syntax/properties` forbids `new Date()` / `Date.now()` in the attendance feature code; displayed times come from API responses formatted with `Intl.DateTimeFormat('de-CH', { timeZone: 'Europe/Zurich' })`.
- Non-Clock-In/Out operations use the server clock, supplied **from `ClockApp.Api`** as plain parameters: the monthly report's default month (server `System.TimeProvider`, converted to Europe/Zurich) and the `CreatedAtUtc` of a correction (the Api controller passes it into `CorrectionService`). The banned-API analyzer therefore still holds inside `ClockApp.Attendance`, and the Clock In/Out code path never receives or reads a local clock.

### Constraint compliance matrix — "every Clock In/Out record uses external Europe/Zurich API time; browser/server clock never used for recording attendance"
| Attendance data | Source of the time | Browser clock | Server clock |
|---|---|---|---|
| `ClockInUtc` / `ClockOutUtc` (employee punches) | External API, fetched inside the request | Never sent, never read (no body, no timestamp header) | Never read |
| `WorkDate` | Zurich local date returned by the API (cross-checked with tz rules, not a clock) | — | — |
| `AttendanceCorrections.CreatedAtUtc` (audit metadata: when an admin made the correction) | Server clock, set in `ClockApp.Api` (not a Clock In/Out record) | — | Allowed |
| Admin-entered corrected in/out times | Typed by the admin (required manual input per the assignment), converted from Zurich local to UTC; stored as `Source=correction` with reason, never presented as a punch; "not in the future" check compares with API time | Not used | Not used |
| "Current month" default and "not in the future" checks for admin corrections | Server clock, evaluated in `ClockApp.Api` (converted to Zurich) | — | Allowed (read-only/validation use, nothing recorded) |
| JWT expiry, invite-token expiry, login rate limiting, logging (not attendance records) | System clock (ASP.NET defaults) in `ClockApp.Api` only | — | Allowed: not attendance data |
If the API is down, Clock In/Out returns 503 and writes nothing — there is no fallback clock. Corrections and reports do not depend on the time API. Verified by the static analyzer, the no-DB-defaults test, the stub-API tests and the frontend lint rule listed above.

### Flow diagram
```
ONBOARDING (admin -> employee)
Admin ──create employee──► Backend ──► DB: Users(PasswordHash=null, InviteTokenHash, Expires)
                              └──► returns one-time link to Admin ──(copy/send)──► Employee
Employee ──opens link, types new password──► Backend: check token hash + expiry + unused
                              └──► DB: save PasswordHash, clear token ──► "Password set, please log in"

LOGIN / PASSWORD CHANGE
Employee/Admin ──email+password──► Backend ──► JWT (role, short expiry)
Logged-in user ──current + new password──► Backend ──► DB: new PasswordHash
Forgot password ──► Admin issues new one-time link (same as onboarding)

CLOCK IN / OUT (employee, JWT required, no timestamp in request)
Employee ──[Clock In]──► Backend ──asks current time──► Time API (Europe/Zurich)
                            │  API down/invalid? ──► 503, nothing saved
                            ▼
                  open record exists? ──yes──► 409 "already clocked in"
                            │no
                            ▼
                  DB: insert AttendanceRecords(ClockInUtc = API time) ──► UI shows time from response
Employee ──[Clock Out]──► same, needs an open record; sets ClockOutUtc (else 409)

ADMIN CORRECTION (admin, JWT required) — never edits the original event
Admin ──record + corrected in/out + reason──► Backend ──► DB: append AttendanceCorrections row
                                                       (original AttendanceRecords row untouched)
Reports/monthly view use the "effective" times = latest correction, else original event
```

## Schema (4 tables + shift row)
```sql
Users(
  Id int identity PK,
  Email nvarchar(256) not null,                    -- UNIQUE
  FullName nvarchar(200) not null,
  PasswordHash nvarchar(256) null,                 -- null until the employee sets own password
  InviteTokenHash nvarchar(64) null,               -- SHA-256 of one-time invite/reset token
  InviteExpiresUtc datetimeoffset null,
  Role nvarchar(20) not null,                      -- 'Admin'|'Employee' (CHECK)
  IsActive bit not null default 1
)

ShiftSettings(                                     -- single row
  Id int PK CHECK (Id = 1),
  DurationMinutes int not null,                    -- default shift duration only, e.g. 510 (8 h 30 min); CHECK 1..1440
  UpdatedByUserId int FK Users
)

AttendanceRecords(                                 -- ONLY genuine Clock In/Out events from the time API
  Id bigint identity PK,
  UserId int not null FK Users,
  ClockInUtc datetimeoffset not null,              -- from external API, never edited
  ClockOutUtc datetimeoffset null,                 -- from external API, set once; never edited
  WorkDate date not null,                          -- Zurich local date of clock-in
  TimeProvider nvarchar(30) not null,              -- which API supplied the time
  IsOpen bit not null,                             -- true until clock-out OR an admin correction closes it
  CONSTRAINT CK_out_after_in CHECK (ClockOutUtc IS NULL OR ClockOutUtc > ClockInUtc)
)
CREATE UNIQUE INDEX UX_OneOpenPerUser ON AttendanceRecords(UserId) WHERE IsOpen = 1;
CREATE INDEX IX_Att_User_Date ON AttendanceRecords(UserId, WorkDate);

AttendanceCorrections(                             -- append-only admin corrections = the audit trail
  Id bigint identity PK,
  Kind nvarchar(10) not null,                      -- 'Adjust' (change a record) | 'Add' (missing record) | 'Void' (cancel record) — CHECK
  AttendanceRecordId bigint null FK AttendanceRecords,  -- null for 'Add'
  UserId int not null FK Users,                    -- the employee concerned
  WorkDate date not null,
  CorrectedClockInUtc datetimeoffset null,         -- Add/Adjust
  CorrectedClockOutUtc datetimeoffset null,
  PreviousClockInUtc datetimeoffset null,          -- effective values just before this correction
  PreviousClockOutUtc datetimeoffset null,
  Reason nvarchar(300) not null,                   -- mandatory, non-blank (CHECK LEN(TRIM(Reason))>=5)
  CreatedByUserId int not null FK Users,           -- the admin
  CreatedAtUtc datetimeoffset not null,            -- when the admin made the correction: server clock (audit metadata, not a Clock In/Out record)
  CONSTRAINT CK_corr_times CHECK (CorrectedClockOutUtc IS NULL OR CorrectedClockInUtc IS NULL OR CorrectedClockOutUtc > CorrectedClockInUtc)
)
```
- **Manual corrections vs actual events**: `AttendanceRecords` holds only events recorded via the time API and is never UPDATEd except the single clock-out set and `IsOpen` flip. Everything an admin does is an appended `AttendanceCorrections` row (never updated/deleted by app code; DB user for the app has no UPDATE/DELETE on it in prod). Effective record = original, overridden by the **latest** correction for that record; `Add` rows are extra effective records flagged "manual" in all UIs/reports; `Void` hides a record from totals.
- Admin closing a forgotten clock-out = `Adjust` correction with a corrected clock-out + same transaction sets `IsOpen = 0`, so the employee can clock in again.
- Corrections are validated: reason required, times consistent, no overlap with other effective records of the user, DST-ambiguous/nonexistent local times rejected, correction of other users' data only by Admin.
- No `DEFAULT` expressions on any time column (see DB guard test).

### Role + password safety
- **Role column**: fine for two fixed roles. Role is read from DB at login and put in a signed JWT; authorization is server-side `[Authorize(Roles=…)]`; clients never send/decide role; no non-admin endpoint accepts `Role`; DB `CHECK` limits values. Short token life (30–60 min) + per-request `IsActive` check so deactivation takes effect quickly.
- **PasswordHash**: only output of ASP.NET `PasswordHasher<TUser>` (PBKDF2, random per-password salt, versioned, supports rehash-on-login); never plaintext/plain SHA; never returned/logged (DTOs); min length 10; login rate limiting (`AddRateLimiter`) + generic error; JWT secret ≥ 32 bytes from env.
- **Admin sets a password vs. gives a "hash"**:
  | Approach | Verdict |
  |---|---|
  | Admin types a password per employee | Admin knows every password; weak/shared passwords; no accountability. Only OK as a temporary password with forced change |
  | Admin hands over / stores a **password hash as the credential** | **Unsafe — rejected.** If the hash authenticates, a DB leak = everyone's credentials (pass-the-hash) |
  | **Admin creates the employee; system generates a random one-time invite token; only its hash is stored; employee sets own password via the link — CHOSEN** | Admin never knows a password; DB leak exposes neither passwords nor usable tokens; same flow for reset |
  Details: token = 32 random bytes (`RandomNumberGenerator`), shown once as `/set-password?token=…`, 24–48 h expiry, stored as SHA-256 (a fast hash is fine for a high-entropy token); `POST /api/auth/set-password` verifies hash + expiry + unused, stores the `PasswordHasher` hash, clears the token; login refused while `PasswordHash` is null. **Later changes**: logged-in `POST /api/auth/change-password` (current + new password); forgotten password → admin issues a new invite. No email/self-service reset in v1. Expiry checks use the system clock (authentication — allowed).

## Missing / multiple punches — options considered; **we chose A**
| Option | How it works | Pros | Cons |
|---|---|---|---|
| **A. Strict reject — CHOSEN** | Clock-in while open → 409; clock-out with none open → 409; a forgotten clock-out stays open until an admin corrects it | Simplest; consistent; DB-enforced | Forgotten clock-out blocks next clock-in until admin fixes; no auto-flagging |
| B. Raw event log, pairs derived in reports | Every tap appended; reports pair events | Never loses a tap | Pairing ambiguous, complex queries, DB cannot enforce validity |
| C. A + flagged anomalies | Open records running longer than default shift duration + grace flagged "MissingOut"; admin resolves with reason; audit | Best explainability; proactive | More work (anomaly query, status, resolve UI) — deferred |
**A → C upgrade is short (~1.5–2.5 h, no data migration)**: the table is already one row per in/out pair, the single-open invariant is already in the DB, and `AttendanceCorrections` already is the audit table. Add: anomalies query (open longer than default duration + grace), admin "anomalies" screen linking to correction, optional employee auto-flag. B would be the only option needing a rewrite.

## Handling a crash / network failure while the frontend sends Clock In or Clock Out
Problem: the browser tab crashes, the connection drops, the request times out, or the user double-clicks/retries. The client cannot know whether the server already recorded the punch (the time API call and DB insert may have completed even though the response never arrived). A blind retry could create a duplicate or a confusing 409; **a client-side queue-and-send-later is not possible**, because the time must be taken from the API at the moment the server receives the punch.
| Option | How it works | Pros | Cons | Effort |
|---|---|---|---|---|
| 1. Do nothing | Strict A rules: retry gets 409 | Zero work | User sees "already clocked in" and doesn't know whether it worked; double-click races rely only on the unique index | 0 |
| **2. UI guard + status check (no idempotency keys) — CHOSEN** | Button disabled while pending; after error/timeout/reload the UI calls `GET /status` (source of truth) and shows the real state ("Recorded at hh:mm" or "Not recorded") before enabling retry. The single-open unique index prevents duplicates; a blind retry gets 409 which the UI translates using `/status` | Simple, cheap, no extra columns, data always correct | A retry after a lost response gets a 409 instead of a clean "OK" (message handled in UI) | ~0.5 h |
| 3. Idempotency key + status check (deferred upgrade) | UI generates a UUID per click (kept in `sessionStorage` until a definitive answer arrives) and sends it as `Idempotency-Key`. Server stores it with the record (`ClockInRequestId`/`ClockOutRequestId`, unique per user). A retry with the **same key** returns the **original result** (HTTP 200, same stored time) instead of 409; a different key while already clocked in → 409. UI also reconciles with `GET /status` after any failure/reload | Safe retries for lost responses/crashes; no duplicates even under races; time still comes only from the server/API (key is not a time) | ~1–1.5 h extra incl. tests | ~1–1.5 h |
| 4. Offline queue | Store punches in the browser and send later | Works offline | **Violates the rule** (server would not get the time at the punch moment, or client time would be trusted) | rejected |

**Server behaviour (chosen option)**: call the time API **before** opening the DB transaction (no locks held during network I/O); in one short transaction insert/update the record; a violation of the single-open unique index (double-click/race) is caught and returned as 409 with the current state; a backend crash before commit leaves nothing written; time API failure → 503 `Retry-After`, nothing written.
**UI behaviour**: states `idle → sending → confirmed | failed | unknown`; on timeout/network error go to `unknown` ("Checking…"), call `GET /status`, then show "Recorded at hh:mm (Zurich)" or "Not recorded — try again"; a 409 is shown using the `/status` data ("already recorded at hh:mm"); on 503 show "Time service unavailable, nothing was recorded"; on page reload fetch `/status` first so the button always reflects the server state; the button is disabled while pending (stops double-click).
**Tests**: two concurrent clock-ins → one 200, one 409 and a single row; clock-out twice → second 409; 503 → no row, later retry succeeds; response lost after commit simulated (cancel after commit) → `/status` shows the record and retry returns 409; frontend Vitest for the state machine (timeout → unknown → status → correct display).
**Deferred upgrade (option 3, ~1.5–2 h, no data loss)**: add `ClockInRequestId`/`ClockOutRequestId` nullable GUID columns with unique indexes and an `Idempotency-Key` header; a retry with the same key replays the stored result (200) instead of 409.

## Edge cases and decisions
| Event | Decision and reasoning | Tests |
|---|---|---|
| **Shift passing midnight** (e.g. 22:00 → 06:00) | A shift is **one record** `[ClockInUtc, ClockOutUtc]`; nothing happens at midnight (no auto-close, no auto-split). `WorkDate` = Zurich date of the **clock-in**, so the whole shift counts on the day it started (and in the month it started). Duration = UTC difference, so a night with a DST change (e.g. 2026-03-29, 7 h real) is correct. Alternative — splitting at midnight into two daily rows — is needed only for per-calendar-day pay rules and adds complexity, so it is deferred | 22:00–06:00 counts once on the start date; shift starting 23:30 on the last day of a month stays in that month; DST night; report shows it as a single entry |
| **Two Clock Outs at the same time** (double-click, two tabs/devices, retry) | Clock-out is an **atomic conditional update**: `UPDATE … SET ClockOutUtc=@apiTime, IsOpen=0 WHERE Id=@id AND UserId=@u AND IsOpen=1`; rows affected = 1 → success, 0 → **409** "already clocked out" (the UI then shows the stored time via `/status`). The first committed clock-out wins and is **never overwritten**; the second request's API time is discarded. (The unique index only protects inserts, so the conditional update is what protects clock-out.) Also reject if `apiTime <= ClockInUtc` (e.g. provider skew) with 503 "time source inconsistent" | Parallel clock-out requests → exactly one 200, one 409, stored time = winner's; clock-out with no open record → 409; clock-out time not after clock-in → rejected |
| **Admin correction crossing another shift** | Every correction (Add/Adjust) is validated against the user's **other effective records** (corrections applied, voided ones ignored). Intervals are compared as UTC half-open ranges `[in, out)`: overlap → rejected with 422 naming the conflicting record; touching at a boundary is allowed; an open record counts as `[in, ∞)`, so a correction overlapping it is rejected until it is closed. Extra guards: `out > in`, maximum length 24 h, not in the future (server clock). **Concurrency**: overlap check + insert (and employee clock-in/out) run under a per-user lock (`sp_getapplock` on `att:{userId}` inside the transaction) so an admin correction and a simultaneous punch can't both succeed into an overlap. A later clock-in whose API time falls inside a corrected interval is rejected with 409 "overlaps a corrected record" | Adjust extending into the next shift → 422; exact touch allowed; Add overlapping an open record → 422; correction vs concurrent clock-in race → one rejected; overnight correction compares correctly across midnight |
These rules fit the existing schedule (+≈0.25 h inside Phases 5–6); admin UI shows the validation message returned by the API.

## Employee monthly report (in scope)
`GET /api/attendance/me/month?year=&month=` (own data only; defaults to current Zurich month): per record `effective duration = out − in` (UTC difference); per day sum of closed effective records by `WorkDate`; `difference = dayTotal − ShiftSettings.DurationMinutes` (+ above / − below); month totals (worked, expected = days worked × default, net difference). Open records shown as "missing clock-out" and **excluded** from totals; corrected/manual entries marked. Response `{ month, defaultShiftMinutes, days:[{ date, records:[{in,out,manual}], workedMinutes, differenceMinutes, hasOpenRecord }], totals }`. UI: "This month" table + month picker. Tests: multiple records/day, exact/above/below default, open record excluded, corrections applied, DST days (2026-03-29, 2026-10-25), month boundary at Zurich midnight.

## Endpoints
- `POST /api/auth/login`, `POST /api/auth/set-password`, `POST /api/auth/change-password`
- Employee: `POST /api/attendance/clock-in`, `POST /api/attendance/clock-out` (no body), `GET /api/attendance/status`, `GET /api/attendance/me/month`
- Admin: `POST/GET /api/admin/employees` (create → one-time invite link; list; deactivate; reissue invite), `GET/PUT /api/admin/settings/shift`, `GET /api/admin/attendance?userId&from&to` (shows original + effective + correction history), `POST /api/admin/attendance/corrections` (Add/Adjust/Void with reason)

## Schedule for 6–8 hours of development (AI-assisted with Claude Code, tests and verification included)
Total **≈ 6.25–8 h**; commit after each phase. To fit, scope is trimmed to the essentials (listed under "Deferred"): a **single time-API adapter** (the `IAttendanceTimeSource` interface stays, so a fallback adapter is a small later addition), corrections limited to **Add/Adjust** (no Void), no `change-password` endpoint (admin reissues an invite instead), one monthly-report view without a month picker, and a focused test suite (clock rule, DST, auth, corrections, monthly report) instead of exhaustive integration coverage. Quality items kept: DST handling, fail-closed time rule + enforcement tests, correction audit trail, role/authorization tests.
| Phase | Work | Est. | Exit criteria |
|---|---|---|---|
| 0 Prep & spike | Tooling in containers (.NET 10 SDK image), db on port 1434, one live call to the chosen time API (fields, key needs, limits), save response fixtures incl. DST samples | 0.5 h | Fixtures saved; adapter mapping decided |
| 1 Scaffold & compose | Solution (Attendance/Api/Tests), Vite React-TS, Dockerfiles, compose, `.env.example`, health endpoint via nginx | 0.5 h | `docker compose up` → 3 healthy containers |
| 2 Data layer | Entities, EF migration, constraints/indexes, seed admin + shift row, DB-no-defaults test | 0.5–0.75 h | Migration applies; schema test green |
| 3 Time source | `IAttendanceTimeSource`, 1 adapter, normalisation + validation, DST/offset logic, fail-closed, banned-API analyzer, stub time-API server for tests | 1–1.25 h | Time-source + DST unit tests green; live smoke call |
| 4 Auth & invite | Login/JWT, role policies, invite token + set-password, login rate limit | 0.75–1 h | Auth tests (403s, token once/expired/reused) green |
| 5 Attendance core (A) | Clock in/out/status, 409 rules, concurrency test on the unique index, lost-response test, forged-timestamp ignored, API-down 503 | 0.75–1 h | Clock-rule and failure-handling tests green |
| 6 Admin & corrections | Employees + invite link, default shift duration, corrections (Add/Adjust, reason, overlap/DST validation, `CreatedAtUtc` from the server clock passed by the Api layer), effective-record logic, history | 0.75–1 h | Correction + audit tests green |
| 7 Monthly report | Calculator + endpoint (server-clock default month) + calculation tests incl. DST/month edges | 0.75–1 h | Report tests green |
| 8 React UI | Login, set-password, role guards, employee page (clock button state machine + status + monthly table), admin pages (employees + invite link, shift duration, attendance with correction dialog/history), ESLint date guard, Vitest for the clock button | 1–1.5 h | Manual end-to-end walk-through OK |
| 9 Verification | Run all checks below on a clean `docker compose up --build`, README | 0.5 h | All checks below pass |
Cut order if time runs short: Vitest component test → admin history view → ESLint date guard (keep analyzer + DB-no-defaults tests).

## Deferred features (explicitly not in this build)
- **Employee correction requests** (employee asks admin to complete a missing record) — options: (1) out-of-band, 0 h **(used now)**; (2) `CorrectionRequests` table + admin approve/reject that creates an `AttendanceCorrections` row, ~2–3 h **(recommended next)**; (3) employee self-edit with review, ~2–3 h (not recommended: unverified times); (4) full workflow with notifications/attachments, 5–8 h.
- Trimmed to fit 6–8 h: second time-API adapter (TimeZoneDB fallback); correction kind `Void`; `change-password` endpoint (admin reissues an invite); month picker in the report; broader full-stack integration test coverage.
- Idempotency keys for clock requests (upgrade described in the failure-handling section); Option C anomaly flagging/screens (see upgrade path above); admin monthly summary per employee; CSV/payroll export; email delivery of invites / self-service forgot-password; per-employee shift overrides; scheduling, leave, geofencing, biometrics, SSO/MFA; WorldTimeAPI third adapter; audit hash-chaining; multi-tenant/multi-location.

## Critical files (to create)
`docker-compose.yml`, `.env.example`, `ClockApp.Attendance/{Entities,AttendanceDbContext,AttendanceService,CorrectionService,MonthlyReport,IAttendanceTimeSource}.cs`, `ClockApp.Attendance/BannedSymbols.txt`, `ClockApp.Api/{Time/TimeApiIoAdapter,Time/TimeZoneDbAdapter,Auth/*,Controllers/*}.cs`, `ClockApp.Tests/**`, `frontend/src/{api,auth,pages,features/attendance}`, `frontend/nginx.conf`, `frontend/eslint.config.js`.

## Verification (end to end)
1. `docker compose up --build` from scratch → db, backend, frontend healthy; `http://localhost:3000` loads; admin seeded.
2. Admin creates employee → one-time link → employee sets password → logs in; reused/expired link fails; DB shows only hashes.
3. Failure handling: kill the connection/response mid-request, reload the page → UI shows the real state from `/status`; a retry gets 409 and the UI explains it from `/status`; no duplicate record exists.
3b. Employee clocks in/out → `ClockInUtc/ClockOutUtc` equal the external API time (compare with an independent API query, and against stub-API test); `TimeProvider` filled; duplicate in/out → 409.
4. Block the time API → clock-in returns 503 and no row is created. Change OS clock by hours → recorded times unchanged.
5. DST: stubbed responses for 2026-03-29 and 2026-10-25 (incl. ambiguous hour) → correct UTC, `WorkDate`, durations; admin correction entering a nonexistent/ambiguous local time is rejected.
6. Admin correction (Adjust/Add/Void) requires a reason, leaves the original event untouched, appears in history with previous values, changes effective totals; employee sees entries marked manual; a correction closing an open record lets the employee clock in again.
7. Monthly view shows per-day totals and +/- difference vs the admin-set default; employee cannot read others' data or call admin routes (403).
8. `dotnet test` (in sdk container) green incl. schema no-defaults test and banned-API build check; `npm run lint && npm test && npm run build` green (lint fails on `new Date()` in attendance code).

## Assumptions
Single company, one default shift for everyone, internal/non-commercial use (TimeZoneDB free terms), SQL Server Developer edition for dev, free-tier API limits re-verified in Phase 0, server clock allowed for authentication/logging, correction `CreatedAtUtc` and the monthly-report default month (never for Clock In/Out records); admin-typed correction times are manual input, distinct from punches.
