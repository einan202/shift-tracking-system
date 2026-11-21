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
- Known gap: the admin attendance view calls `EffectiveAttendance.LoadAsync` without voided rows, so deleted shifts vanish from the admin screen until 1.7 adds the history variant.
- Lint: the `set-state-in-effect` warning in `Timesheet.tsx` is fixed. Four older warnings remain in `auth.tsx`, `ClockPanel.tsx`, `Employees.tsx`, `AttendanceAdmin.tsx` (same pattern, pre-existing).

## 1.4b (partial) and admin-history fix
- Done: concurrency test for an employee shift edit racing the employee's own clock-in (30 rounds, service level, app-user connection). [Eng] Either order is legal: if the punch lands first, an edit of the same day adjusts that punch; otherwise the manual shift wins and the punch is refused (`OverlapsExisting`). The invariant tested: never two overlapping effective records.
- Bug fixed (user report): admin history of a manually added shift showed only the `Add` row because later `Adjust` rows reference it through `TargetCorrectionId` (added in 1.1) and the admin endpoint only matched on the Add id. Now both are listed in order; regression test added. The UI no longer prints "was open – open" for `Add` rows.
- NOT done yet: the post-clock-out prompt for stale shifts, DST offset selection in the dialog (limitation unchanged: ambiguous times are rejected with 422), and the long-shift timer format. The backend rules for "needs correction", the 24 h cap on corrected intervals and raw-span overlap already exist and are tested (1.3/1.4).
- Admin attendance table mirroring the employee timesheet (consolidated day + per-interval history) is iteration 1.7, not started.
