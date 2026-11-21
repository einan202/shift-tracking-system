# Backlog

Known limitations, technical debt and future features that are **not** implemented. Source: PR #1 review items 6-9 and 11. Priority and scope are not decided; entries describe the current state only. Architectural decisions are recorded separately in `DECISIONS.md`.

| # | Type | Item | Current state |
|---|---|---|---|
| 6 | Technical debt | Browser-clock guard is a source scan, not a lint rule | `frontend/src/features/attendance/noBrowserClock.test.ts` fails on `Date.now()`, `new Date()` and `performance.now()` in app sources. oxlint has no matching rule, and the regex would miss forms such as `Date['now']()`. |
| 7 | Technical debt | No component tests for the admin screens | `Employees.tsx` and `AttendanceAdmin.tsx` have no tests. Covered today: the clock panel, the correction dialog, the punch logic, the invite link, and the backend endpoints they call. |
| 8 | Limitation | time.now attribution not added | time.now's page asks for attribution when using its free API. Nothing is shown in the app or README. It also publishes no numeric rate limit (a punch makes 1 request, at most 4). |
| 9 | Limitation | Corrections are minute-granular | The admin form takes minutes only, so a shift cannot be closed within the minute it started (the server answers "Clock-out must be after clock-in"). |
| 11 | Future feature | Idempotency keys for clock requests | Not built. A retry after a lost response gets a 409; the UI explains it using `/status`. |
| 11 | Future feature | Change-password endpoint | Not built. A forgotten or replaced password needs a new invite link from an admin. |
| 11 | Future feature | Anomaly flagging for forgotten clock-outs | Not built. An open record stays open until an admin corrects it; nothing flags it. |
| 11 | Future feature | Month picker | Not built. The monthly report and the admin attendance view have previous/next month buttons only. |
| F1 | Future feature | Shift-Based Scheduling | Admins define individual shifts with specific start and end times, multiple shifts per day, and expected hours calculated from the scheduled time ranges. Today: working weekdays with one expected duration each, no fixed times. |
| F2 | Future feature | Automatic holiday API integration | Fetch public holidays automatically (for example Nager.Date or OpenHolidays for Switzerland, Hebcal for Israel), with country/region calendars, caching and manual override. Today: holidays are managed manually by the admin (planned, Milestone 2). |
| F3 | Future feature | Leave & Absence Management | Sick leave and vacation days, other absence types, employee leave requests and admin approval, leave balances and entitlement tracking, separation of worked, approved-absence and payable hours, impact on monthly summaries and expected hours. Today: only a simple absence reason at monthly submission (planned, Milestone 2). |
| F4 | Future feature | Advanced escalation workflow | Escalation notes and levels, derived "due soon / past the suggested date" states and flags, a richer month-close board (filters, sorting, bulk actions, per-month edit counts). Today: informational suggested dates only. |
| F5 | Future feature | Admin-configurable deadlines and reminders | Configurable submission and approval deadlines; email or push reminders. |
| F6 | Future feature | Hourly pay type | Stored as a pay type but disabled in the UI. |
| F7 | Future feature | Retroactive schedule corrections | Correcting a past month's schedule needs an approval flow and re-computation of snapshots; schedule changes are prospective only. |
| F8 | Limitation | No UI to change an employee's schedule or see its history (Iteration 1.2) | The backend `POST/GET /admin/employees/{id}/schedule` appends and lists schedule versions (effective date on or after the first of the current Zurich month), but `Employees.tsx` only sends a schedule on creation. Found in the independent review. |
| F9 | Limitation | No way to edit an employee's start date (Iteration 1.2) | The start date is set at creation only. The plan's rules (a start after the first attendance is rejected, an earlier start warns) do not exist; only edits before the start date are blocked. |
| F10 | Limitation | The shift edit dialog cannot set a next-day clock-out | The dialog builds both times on the edited day and sends no UTC offset, so a clock-out after midnight, and times in the repeated DST hour, cannot be entered (the latter gets a 422). Part of the 1.4b remainder. |
