# Implementation notes (Milestone 1: employee-owned attendance)

Facts and engineering decisions made while building `.claude/plan-attendance-day-corrections.md`.
**Approved requirements** come from the plan and the user's messages; **[Eng]** marks decisions I made on my own.
`DECISIONS.md` is untouched (needs the user's approval). Deferred work is in `docs/BACKLOG.md`.

## 1.0 Navigation (frontend only)
- [Eng] Own ~30-line `popstate` router (`router.ts`) instead of a library: no new dependency, matches the plan.
- Role guard redirects to the role's default tab; modified clicks left to the browser.
- Verified live at 375/1440 px, Back/Forward, deep links. Commits: see `git log` (1.0 series).

## 1.1 Schema foundation
- New table `EmployeeScheduleVersions` (append-only, SELECT+INSERT only for `clockapp_app`, permission test).
- `AttendanceCorrections`: `Void` kind, `TargetCorrectionId`, `BatchId`, `ReasonCode NOT NULL`; legacy rows backfilled `Other`; CHECKs for kind/target/reason.
- `Users.PayType`, `Users.EmploymentStartDate` backfilled; one initial schedule version per employee.
- [Eng] Edited the generated migration (EF emitted `""` defaults that violate the CHECKs; temp default dropped so the "no defaults on attendance tables" test stays green).
- [Eng] `IMonthStateReader` seam with `AlwaysOpenMonthState` so Milestone 2 locking needs no change in edit code.
- Live migration verified: 37 corrections preserved, 38 employees backfilled.

## 1.2 Schedules and employee configuration
- Effective-dated versions; a change may start at the first day of the current Zurich month or later (past months never recalculated).
- [Eng] `ScheduleResolver` is one pure function used by every calculation; dates before the first version use the earliest version.

## 1.3 Timesheet
- `MonthSheet` is pure (no clock; "today" is a parameter). Later-date clock-outs and >24 h intervals are "needs correction" and count nothing.
- Endpoint `GET /attendance/me/timesheet` exposes only per-field manual flags (no reasons, history, providers).
- [Eng] Added a screen-reader-only "Actions" column next to the 7 agreed columns for the Edit/Add buttons.
- [Eng] Old `/me/month` endpoint kept (still tested); remove in a later cleanup.

## 1.4 Employee set-shift
- `ShiftEditService`: first clock-in / last clock-out adjusted, or a manual interval for an empty day; punches never altered; rows share a `BatchId`; planning is pure and also drives the preview; commit runs under `UserLock` in one transaction.
- Rules: out > in, <= 24 h, not future, same Zurich day, no overlap (open shift counts to infinity), not before employment start, month must be Open.
- [Eng] Edits of days before the employment start date are rejected (user request during review).
- [Eng] Clock-in is refused on days not in the employee's schedule; clock-out is never blocked (user request). Employees without schedule versions are unrestricted.
- Error messages name the conflicting existing time (user feedback).
- Limitation: the dialog does not yet send a UTC offset for the repeated DST hour; the server rejects ambiguous times (422) rather than guessing.
- Verified: backend 171 passed / 2 skipped, frontend 46 passed, `verify-all` green, browser check done for add shift.

## Technical debt
- Admin attendance view still uses the old per-record correction model until 1.7.
- No concurrency test yet for shift edits racing the employee's own punch (planned in 1.4b).

## 1.5 Delete shift
- `POST /attendance/me/shifts/delete` (+ `/delete/preview`): appends one `Void` correction per effective interval of the date (shared `BatchId`, reason code/text, employee as actor). Punches and earlier corrections are never touched; admins can still see the history (their view is rebuilt in 1.7).
- Rules: reason required (Other needs text), blocked while any interval of the day is open, no future days, nothing before the employment start date, month must be Open, empty day rejected. Runs under `UserLock` in one transaction.
- [Eng] Delete is a separate endpoint (not an option of set-shift) so the alert dialog and the audit rows stay unambiguous.
- UI: Delete button on finished days, alert dialog stating the consequence (day counts as no work, balance becomes the negative expected) and a reason.
- Deleted shifts are shown to admins (flagged "deleted", struck through, with the Void in the history).
- Lint: the `set-state-in-effect` warning in `Timesheet.tsx` is fixed. Four older warnings remain in `auth.tsx`, `ClockPanel.tsx`, `Employees.tsx`, `AttendanceAdmin.tsx` (same pattern, pre-existing).

## 1.4b (partial) and admin-history fix
- Done: concurrency test for an employee shift edit racing the employee's own clock-in (30 rounds, service level, app-user connection). [Eng] Either order is legal: if the punch lands first, an edit of the same day adjusts that punch; otherwise the manual shift wins and the punch is refused (`OverlapsExisting`). The invariant tested: never two overlapping effective records.
- Bug fixed (user report): admin history of a manually added shift showed only the `Add` row because later `Adjust` rows reference it through `TargetCorrectionId` (added in 1.1) and the admin endpoint only matched on the Add id. Now both are listed in order; regression test added. The UI no longer prints "was open – open" for `Add` rows.
- NOT done yet: the post-clock-out prompt for stale shifts, DST offset selection in the dialog (limitation unchanged: ambiguous times are rejected with 422), and the long-shift timer format. The backend rules for "needs correction", the 24 h cap on corrected intervals and raw-span overlap already exist and are tested (1.3/1.4).
- Admin attendance table mirroring the employee timesheet (consolidated day + per-interval history) is iteration 1.7, not started.

## Final state and known limitations (submission)
- **Deleted shifts in the admin view:** shown (flagged "deleted", struck through, Void row in the history) since `9c8f99c`/`23fd93f`. Covered by an API test; **not yet checked visually in the browser**. (Before those commits they were invisible to admins.)
- **Inconsistency, behaviour unchanged pending your decision:** clock-in is refused on days outside the employee's schedule, but an employee can still *add* a shift on such a day through the timesheet "Add" button (with a reason). Intended requirement not yet confirmed.
- **Not implemented:** post-clock-out prompt for stale shifts (the backend "needs correction" rules exist), DST offset selection in the dialog (ambiguous fall-back hour is rejected with 422), live Clock Out timer (1.6), admin attendance table mirroring the employee layout and retiring admin corrections (1.7), Milestone 2 and 3.
- **Admin correction dialog/endpoint still exist** (old per-record model, `ReasonCode = Other`); employees use the new endpoints.
- **Lint:** four pre-existing `set-state-in-effect`/fast-refresh warnings remain (`auth.tsx`, `ClockPanel.tsx`, `Employees.tsx`, `AttendanceAdmin.tsx`).
- **Mutation checks** were not run for the newest tests (admin history, delete).
- **Local dev setup:** dev passwords and the login token in sessionStorage, as in earlier phases.

## Additional requirement: independent expected hours per weekday
- Already provided by the existing effective-dated schedule (no new scheduling architecture): seven independent `ExpectedMinutes*` per version, any combination of days, company default only prefills a newly ticked day, `ScheduleResolver`/`MonthSheet` use each date's own weekday value for Expected, Balance and totals.
- Added verification only: pure test with Mon 8h30 / Tue 9h / Wed 7h45 / Thu 8h / Fri 6h (Expected per day, balances, whole-month total 10470 min), API round-trip with five different durations plus a later-dated change of one day keeping the others, and a form test that editing Wednesday leaves the other days untouched. The admin employee form was checked in the browser (seven independent rows, default 9h from settings).
- Not done: a separate "redesigned" compact form; the current form is the 1.2 layout (seven rows with checkbox, hours, minutes).

## Admin UI/UX pass and per-weekday company default (requested after the checkpoint)
Approved by the user in chat; the palette and design language are unchanged.
- **Company default is now per weekday (schema change).** Migration `ShiftSettingsPerWeekday` adds seven `ExpectedMinutes*` columns (+ check constraint) to the single `ShiftSettings` row; existing value carried over (Mon–Fri = the old single duration, weekend 0). `DurationMinutes` is kept (legacy, synced to the first working day) because the old `/me/month` report still reads it. `PUT /admin/settings/shift` accepts `minutes[7]` (preferred) or the legacy `durationMinutes` (= Mon–Fri); `GET` returns both. New employees without an explicit schedule get the company week. Existing employees are never touched. No new grant needed (the row was already updatable).
- **Settings screen** now edits the whole default week with the same weekday grid (title "Default working week").
- **Employees screen:** the list is the main content with a primary "Add employee" action; the form moved into a modal (focus on the first field, Escape closes, the one-time invite link appears inside the modal, "Done" closes). [Eng] Rows become stacked cards below 720 px so the Deactivate/New link actions stay reachable; email moves under the name.
- **Weekday grid:** day cells with checkbox and h/m inputs, wrapping to fit (`auto-fit`, min 7.75 rem). First version clipped the number inputs; fixed by hiding spinners and a minimum input width.
- **Login / set-password:** "Shift Tracking" is the brand title above the card (matching the top bar and tab title); the form is a card with a "Sign in" heading; behaviour unchanged.
- [Eng] Layout bug fixed: wide tables widened the whole page because grid children could not shrink (`.stack > *, .panel { min-width: 0 }`).
- Checked in the browser at ~1440, ~830 and 375 px (employees list and modal, settings, login at desktop width). Not captured: login at 375 px and the attendance/timesheet screens were not re-reviewed; no further density changes were made there.
- Known minor issue (pre-existing): when the admin token is stale the employees list shows "No employees yet" instead of an error.

### Scale-back of the UI pass (user decision: preserve the existing design)
- Weekday grid: kept compact and wrapping, but plain cells (no per-day border/background; the "off" state is colour only).
- Login: original plain layout and "Sign in" heading restored; "Shift Tracking" stays as the title above it (left-aligned).
- Employee count badge removed.
- Phones: the stacked-card table was replaced by the normal table with the Starts/Email/Status columns folded under the name and a stacked actions column; no horizontal overflow at 375 px, all actions visible.
- Unchanged: Add-employee modal, Settings per-weekday default, page-width fix, all functionality.

## Admin Attendance page: targeted layout fixes (option A)
Frontend only (`AttendanceAdmin.tsx`, `index.css`); same data, API, colours, tags and correction dialog.
- Toolbar fits one row on desktop (employee, month stepper, "Add missing record"); on phones the employee select takes the full row.
- Rows are grouped by day: the date shows once, later records of the same day leave it blank (kept as screen-reader text).
- Columns renamed: "Punched (API time)" and "Counted as"; the actions column has a hidden "Actions" label.
- Phones: the Punched column folds under the counted time, nothing is clipped, no horizontal scroll at 375 px.
- Checked in the browser at 375 and 1280 px; no console errors.
- Not done (option B, backlog): the employee-timesheet layout for admins (Date/Day/Worked/Expected/Balance, month summary, per-day history) needs a new endpoint (iteration 1.7).
